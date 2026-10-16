#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit

/// Pairing over the real channels: TLS data channel from the QR code, then the Bluetooth bond
/// through AccessorySetupKit, then the PC's `Hello` on the BLE channel.
@MainActor
public final class LivePairing: PairingProviding {
    /// Why pairing stopped.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// None of the QR code's addresses answered.
        case noReachableAddress

        /// The PC answered with something other than `PairingResult`.
        case unexpectedReply

        /// The PC declined.
        case rejected(Fenpo_V1_PairingFailure)

        /// `PairingResult` does not match the QR code or lacks the BLE key.
        case invalidResult

        /// A later step was called before the data channel step succeeded.
        case notPaired
    }

    private let identityStore: DeviceIdentityStore
    private let store: PairedPCStore
    private nonisolated let central: BLECentral
    private let accessories: AccessorySetup
    private let log = FenpoLog("Pairing")

    /// Creates the pairing flow.
    public init(identityStore: DeviceIdentityStore, store: PairedPCStore, central: BLECentral, accessories: AccessorySetup) {
        self.identityStore = identityStore
        self.store = store
        self.central = central
        self.accessories = accessories
    }

    public nonisolated func startDataChannel(scannedCode: String) async throws -> PairedPC {
        try await runDataChannel(scannedCode)
    }

    public nonisolated func bondBluetooth() async throws {
        try await runBluetoothBond()
    }

    public nonisolated func waitForNotificationSharing() async throws {
        // The PC sends PcStatus after its Hello and again when the user allows notification sharing.
        let central = central
        try await withTimeout(.seconds(180)) {
            try await central.waitForNotificationsSubscribed()
        }
    }

    /// Keys the BLE channel for the stored pairing and starts reconnecting. Call at launch.
    public func resumeChannel() {
        guard let record = store.load(), let identifier = record.bluetoothIdentifier else {
            return
        }
        do {
            try configureChannel(for: record)
            central.connect(to: identifier)
        } catch {
            log.error("Resuming the BLE channel failed: \(String(describing: error))")
        }
    }

    // MARK: - Steps

    private func runDataChannel(_ scannedCode: String) async throws -> PairedPC {
        let code = try PairingQRCode(parsing: scannedCode)
        let identity = try identityStore.loadOrCreate()

        var lastError: Swift.Error = Error.noReachableAddress
        for endpoint in code.endpoints {
            let connection = TLSControlConnection(endpoint: endpoint, pinnedServerSHA256: code.certificateSHA256, identity: identity)
            do {
                try await withTimeout(.seconds(8)) {
                    try await connection.start()
                }
            } catch {
                log.info("No TLS to \(endpoint.description): \(String(describing: error))")
                lastError = error
                await connection.cancel()
                continue
            }
            defer {
                Task {
                    await connection.cancel()
                }
            }
            return try await exchange(on: connection, code: code)
        }
        throw lastError
    }

    private func exchange(on connection: TLSControlConnection, code: PairingQRCode) async throws -> PairedPC {
        var sequencer = EnvelopeSequencer()

        var request = Fenpo_V1_PairingRequest()
        request.token = Data(code.token)
        request.deviceID = LocalDevice.deviceID
        request.deviceName = UIDevice.current.name
        request.platform = .ios
        try await connection.send(sequencer.envelope(.pairingRequest(request)))

        // The PC may ask its user to confirm, so allow time.
        let reply = try await withTimeout(.seconds(90)) {
            try await connection.receive()
        }
        guard case .pairingResult(let result) = reply.body else {
            throw Error.unexpectedReply
        }
        guard result.accepted else {
            throw Error.rejected(result.failure)
        }
        guard result.pcID == code.pcID, result.bleChannelKey.count == 32 else {
            throw Error.invalidResult
        }

        let name = result.pcName.isEmpty ? code.pcName : result.pcName
        try store.save(
            PairedPCRecord(pcID: code.pcID, name: name, certificateSHA256: code.certificateSHA256, endpoints: code.endpoints.map(\.description)),
            bleChannelKey: Array(result.bleChannelKey)
        )
        log.info("Paired with \(name)")

        // S5: the pairing connection continues as a normal session with Hello.
        var systemName = ""
        let hello = makeHello()
        do {
            try await connection.send(sequencer.envelope(.hello(hello)))
            let answer = try await withTimeout(.seconds(10)) {
                try await connection.receive()
            }
            let session = try Handshake.negotiate(local: hello, remote: answer)
            systemName = session.peerPlatform == .windows ? "Windows" : ""
        } catch {
            log.error("Hello after pairing failed: \(String(describing: error))")
        }
        return PairedPC(id: code.pcID, name: name, systemName: systemName)
    }

    private func runBluetoothBond() async throws {
        guard let record = store.load() else {
            throw Error.notPaired
        }
        let image = UIImage(systemName: "laptopcomputer") ?? UIImage()
        let accessory = try await accessories.pickPC(named: record.name, image: image)
        guard let identifier = accessory.bluetoothIdentifier else {
            throw AccessorySetup.Error.missingBluetoothIdentifier
        }

        var updated = record
        updated.bluetoothIdentifier = identifier
        try store.update(updated)
        try configureChannel(for: updated)
        central.connect(to: identifier)

        let central = central
        try await withTimeout(.seconds(120)) {
            try await central.waitUntilReady()
        }
    }

    private func configureChannel(for record: PairedPCRecord) throws {
        guard let key = try store.bleChannelKey(for: record.pcID) else {
            throw Error.notPaired
        }
        let cipher = try AESGCMChannelCipher(key: key, counters: try store.counters(for: record.pcID))
        let store = store
        let pcID = record.pcID
        let log = log
        central.configure(cipher: cipher, hello: makeHello()) { counters in
            do {
                try store.saveCounters(counters, for: pcID)
            } catch {
                log.error("Saving BLE counters failed: \(String(describing: error))")
            }
        }
    }

    private func makeHello() -> Fenpo_V1_Hello {
        LocalDevice.makeHello()
    }
}
#endif
