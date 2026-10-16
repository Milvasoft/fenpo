#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import OSLog

/// Session state from the stored pairing and the BLE channel.
///
/// The Wi-Fi control connection, clipboard previews and transfer history arrive with their
/// features; until then those parts of the snapshot stay empty.
@MainActor
public final class LiveSession: SessionProviding {
    private let store: PairedPCStore
    private let central: BLECentral
    private let accessories: AccessorySetup
    private let logger = Logger(subsystem: "com.fenpo.kit", category: "Session")

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
                logger.error("Removing the accessory failed: \(String(describing: error), privacy: .public)")
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

    private init() {
        identityStore = DeviceIdentityStore()
        pairedStore = PairedPCStore()
        central = BLECentral()
        accessories = AccessorySetup()
        pairing = LivePairing(identityStore: identityStore, store: pairedStore, central: central, accessories: accessories)
        session = LiveSession(store: pairedStore, central: central, accessories: accessories)
    }

    /// Activates AccessorySetupKit and reconnects to the paired PC.
    public func start() {
        accessories.activate()
        pairing.resumeChannel()
    }
}
#endif
