#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit

/// Session state from the stored pairing and the BLE channel.
///
/// The Wi-Fi control connection, clipboard previews and transfer history arrive with their
/// features; until then those parts of the snapshot stay empty.
@MainActor
public final class LiveSession: SessionProviding {
    private let store: PairedPCStore
    private let central: BLECentral
    private let accessories: AccessorySetup
    private let log = FenpoLog("Session")

    /// Creates the session.
    public init(store: PairedPCStore, central: BLECentral, accessories: AccessorySetup) {
        self.store = store
        self.central = central
        self.accessories = accessories
    }

    public nonisolated func snapshot() async -> SessionSnapshot {
        await makeSnapshot()
    }

    public nonisolated func lockPC() async throws {
        try await sendLock()
    }

    public nonisolated func unpair() async throws {
        try await removePairing()
    }

    private func makeSnapshot() -> SessionSnapshot {
        guard let record = store.load() else {
            return .unpaired
        }
        return SessionSnapshot(
            pc: PairedPC(id: record.pcID, name: record.name, systemName: "Windows"),
            links: ChannelLinks(bluetooth: central.state == .ready, wifi: false, usb: false),
            proximity: .unknown,
            phoneClipboardPreview: nil,
            pcClipboardPreview: nil,
            readyPhotoCount: 0,
            transfers: []
        )
    }

    private func sendLock() throws {
        try central.send(.lockPc(Fenpo_V1_LockPc()))
    }

    private func removePairing() async throws {
        let identifier = store.load()?.bluetoothIdentifier
        central.disconnect()
        try store.remove()
        if let identifier {
            do {
                try await accessories.remove(bluetoothIdentifier: identifier)
            } catch {
                log.error("Removing the accessory failed: \(String(describing: error))")
            }
        }
    }
}

/// Everything that must exist once per process: created at launch so BLE state restoration works.
@MainActor
public final class FenpoServices {
    /// The one instance.
    public static let shared = FenpoServices()

    public let identityStore: DeviceIdentityStore
    public let pairedStore: PairedPCStore
    public let central: BLECentral
    public let accessories: AccessorySetup
    public let pairing: LivePairing
    public let session: LiveSession
    public let clipboard: PCClipboardReceiver
    public let actions: LiveFenpoActions
    public let ringer: PhoneRinger
    public let statusStore: SharedStatusStore
    public let browser = PCBrowser()

    /// Called whenever the channel state or the PC's status changes.
    public var onChange: (() -> Void)?

    private var pcBattery: (percent: Int, charging: Bool)?
    private let log = FenpoLog("Services")

    private init() {
        identityStore = DeviceIdentityStore()
        pairedStore = PairedPCStore()
        central = BLECentral()
        accessories = AccessorySetup()
        pairing = LivePairing(identityStore: identityStore, store: pairedStore, central: central, accessories: accessories)
        session = LiveSession(store: pairedStore, central: central, accessories: accessories)
        clipboard = PCClipboardReceiver()
        actions = LiveFenpoActions(store: pairedStore, central: central)
        ringer = PhoneRinger()
        statusStore = SharedStatusStore()

        central.onEnvelope = { [weak self] envelope in
            self?.route(envelope)
        }
        central.onStateChange = { [weak self] state in
            if state != .ready {
                self?.pcBattery = nil
            }
            self?.publishStatus()
        }
    }

    /// Activates AccessorySetupKit and reconnects to the paired PC.
    public func start() {
        FenpoActionsRegistry.shared.register(actions)
        accessories.activate()
        pairing.resumeChannel()
        publishStatus()
    }

    /// Writes the widget status and tells the app.
    public func publishStatus() {
        let record = pairedStore.load()
        statusStore.save(SharedStatus(
            pcName: record?.name,
            isConnected: record != nil && central.state == .ready,
            pcBatteryPercent: pcBattery?.percent,
            pcCharging: pcBattery?.charging
        ))
        onChange?()
    }

    private func route(_ envelope: Fenpo_V1_Envelope) {
        switch envelope.body {
        case .clipboard(let item):
            clipboard.handle(item)
        case .deviceState(let state):
            apply(state)
        case .ring(let request):
            ringer.handle(request)
        case .openURL(let message):
            openFromPC(message.url)
        default:
            break
        }
    }

    private func apply(_ state: Fenpo_V1_DeviceState) {
        if state.hasBatteryPercent {
            pcBattery = (Int(state.batteryPercent), state.hasCharging && state.charging)
        }
        if !state.addresses.isEmpty, var record = pairedStore.load() {
            record.endpoints = state.addresses
            try? pairedStore.update(record)
        }
        log.info("PC state: battery \(state.hasBatteryPercent ? "\(state.batteryPercent)%" : "unknown"), \(state.addresses.count) address(es)")
        publishStatus()
    }

    /// "Open on iPhone": only possible while Fenpo is in front; iOS does not let a background app open links.
    private func openFromPC(_ text: String) {
        guard let url = URL(string: text), let scheme = url.scheme?.lowercased(), ["http", "https"].contains(scheme) else {
            log.error("Ignored OpenUrl with an unsupported link")
            return
        }
        guard UIApplication.shared.applicationState == .active else {
            log.info("OpenUrl arrived in the background; not opened")
            return
        }
        UIApplication.shared.open(url)
        log.info("Opened a link from the PC")
    }

    // MARK: - Device tests (Developer menu)

    /// Pairs with the macOS test peripheral: fixed key, AccessorySetupKit picker, bond, the tool's Hello.
    public func pairWithTestPeripheral() async throws {
        log.info("Test pairing started")
        try pairedStore.save(
            PairedPCRecord(pcID: TestPeripheral.pcID, name: TestPeripheral.name, certificateSHA256: [], endpoints: []),
            bleChannelKey: TestPeripheral.bleChannelKey
        )
        try await pairing.bondBluetooth()
        try await pairing.waitForNotificationSharing()
        log.info("Test pairing finished")
    }

    /// Sends a Ping over BLE.
    public func sendPing() throws {
        var ping = Fenpo_V1_Ping()
        ping.nonce = UInt64.random(in: 1...UInt64.max)
        try central.send(.ping(ping))
        log.info("Ping sent")
    }

    /// Forgets the pairing and the phone's TLS identity.
    public func resetEverything() async throws {
        log.info("Reset requested")
        try await session.unpair()
        try identityStore.delete()
    }

    /// What the Developer menu shows.
    public func diagnostics() -> Diagnostics {
        let record = pairedStore.load()
        let counters = record.flatMap { try? pairedStore.counters(for: $0.pcID) }
        return Diagnostics(
            bluetoothState: String(describing: central.state),
            peerName: central.session?.peerName,
            capabilities: central.session.map { $0.capabilities.map { String(describing: $0) }.sorted() } ?? [],
            pcName: record?.name,
            pcID: record?.pcID,
            bluetoothIdentifier: record?.bluetoothIdentifier?.uuidString,
            counters: counters,
            deviceID: LocalDevice.deviceID,
            appVersion: LocalDevice.appVersion,
            build: Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "?"
        )
    }

    /// Snapshot for the Developer menu.
    public struct Diagnostics: Equatable, Sendable {
        public let bluetoothState: String
        public let peerName: String?
        public let capabilities: [String]
        public let pcName: String?
        public let pcID: String?
        public let bluetoothIdentifier: String?
        public let counters: BLEChannelCounters?
        public let deviceID: String
        public let appVersion: String
        public let build: String
    }
}
#endif
