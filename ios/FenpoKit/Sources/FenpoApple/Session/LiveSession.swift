#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit

/// Session state from the stored pairing, the BLE channel, the Wi-Fi channel and the transfers.
@MainActor
public final class LiveSession: SessionProviding {
    private let store: PairedPCStore
    private let central: BLECentral
    private let wifi: WiFiChannel
    private let transfers: FileTransferService
    private let transferStore: TransferStore
    private let accessories: AccessorySetup
    private let log = FenpoLog("Session")

    /// Creates the session.
    public init(store: PairedPCStore, central: BLECentral, wifi: WiFiChannel, transfers: FileTransferService, transferStore: TransferStore, accessories: AccessorySetup) {
        self.store = store
        self.central = central
        self.wifi = wifi
        self.transfers = transfers
        self.transferStore = transferStore
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
            links: ChannelLinks(bluetooth: central.state == .ready, wifi: wifi.state == .connected, usb: false),
            proximity: .unknown,
            phoneClipboardPreview: nil,
            pcClipboardPreview: nil,
            readyPhotoCount: 0,
            transfers: transferStore.load(),
            activeTransfers: transfers.active.values.sorted { $0.id < $1.id }
        )
    }

    private func sendLock() throws {
        try central.send(.lockPc(Fenpo_V1_LockPc()))
    }

    private func removePairing() async throws {
        let identifier = store.load()?.bluetoothIdentifier
        central.disconnect()
        wifi.disconnect()
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
    /// Which channel a message came in on, so replies go back the same way.
    public enum Channel: Sendable {
        case bluetooth
        case wifi
    }

    /// The one instance.
    public static let shared = FenpoServices()

    public let identityStore: DeviceIdentityStore
    public let pairedStore: PairedPCStore
    public let central: BLECentral
    public let wifi: WiFiChannel
    public let transfers: FileTransferService
    public let transferStore: TransferStore
    public let accessories: AccessorySetup
    public let pairing: LivePairing
    public let session: LiveSession
    public let clipboard: PCClipboardReceiver
    public let actions: LiveFenpoActions
    public let ringer: PhoneRinger
    public let statusStore: SharedStatusStore
    public let camera: CameraService
    public let browser = PCBrowser()

    /// Called whenever the channel state, the PC's status or a transfer changes.
    public var onChange: (() -> Void)?

    /// Handlers for messages other services own (for example the camera), tried before the defaults.
    public var extraHandlers: [(Fenpo_V1_Envelope, Channel) -> Bool] = []

    private var pcBattery: (percent: Int, charging: Bool)?
    private var isActive = false
    private let log = FenpoLog("Services")

    private init() {
        identityStore = DeviceIdentityStore()
        pairedStore = PairedPCStore()
        central = BLECentral()
        wifi = WiFiChannel(client: DataChannelClient(store: pairedStore, identityStore: identityStore))
        transferStore = TransferStore()
        transfers = FileTransferService(wifi: wifi, store: transferStore)
        accessories = AccessorySetup()
        pairing = LivePairing(identityStore: identityStore, store: pairedStore, central: central, accessories: accessories)
        session = LiveSession(store: pairedStore, central: central, wifi: wifi, transfers: transfers, transferStore: transferStore, accessories: accessories)
        clipboard = PCClipboardReceiver()
        actions = LiveFenpoActions(store: pairedStore, central: central, wifi: wifi, transfers: transfers)
        ringer = PhoneRinger()
        statusStore = SharedStatusStore()
        camera = CameraService(wifi: wifi)

        central.onEnvelope = { [weak self] envelope in
            self?.route(envelope, from: .bluetooth)
        }
        central.onStateChange = { [weak self] state in
            if state != .ready {
                self?.pcBattery = nil
            }
            self?.publishStatus()
        }
        wifi.onEnvelope = { [weak self] envelope in
            self?.route(envelope, from: .wifi)
        }
        central.onHello = { [weak self] in
            self?.actions.resendFocus(over: .bluetooth)
        }
        wifi.onStateChange = { [weak self] state in
            if state == .connected {
                self?.actions.resendFocus(over: .wifi)
            }
            self?.onChange?()
        }
        transfers.onChange = { [weak self] in
            self?.onChange?()
        }
        camera.onChange = { [weak self] in
            self?.onChange?()
        }
    }

    /// Activates AccessorySetupKit and reconnects to the paired PC.
    public func start() {
        FenpoActionsRegistry.shared.register(actions)
        accessories.activate()
        pairing.resumeChannel()
        publishStatus()
    }

    /// Fenpo came to the front: open the Wi-Fi channel so the PC can send files and start the camera.
    public func appBecameActive() {
        isActive = true
        connectWiFiIfUseful()
        camera.appBecameActive()
    }

    /// Fenpo went to the background: iOS suspends sockets, so close the channel unless a transfer runs.
    public func appEnteredBackground() {
        isActive = false
        if camera.state != nil {
            // iOS stops the camera in the background; tell the PC instead of freezing its picture.
            camera.stopFromPhone()
        }
        if transfers.active.isEmpty {
            wifi.disconnect()
        }
    }

    /// Sends a message on a channel, for replies.
    /// - Parameters:
    ///   - body: Message.
    ///   - replyTo: Id of the request it answers.
    ///   - channel: Channel the request came on.
    public func reply(_ body: Fenpo_V1_Envelope.OneOf_Body, to replyTo: UInt64, on channel: Channel) {
        switch channel {
        case .bluetooth:
            try? central.send(body, replyTo: replyTo)
        case .wifi:
            Task {
                _ = try? await wifi.send(body, replyTo: replyTo)
            }
        }
    }

    /// Writes the widget status and tells the app.
    public func publishStatus() {
        let record = pairedStore.load()
        statusStore.save(SharedStatus(
            pcName: record?.name,
            isConnected: record != nil && (central.state == .ready || wifi.state == .connected),
            pcBatteryPercent: pcBattery?.percent,
            pcCharging: pcBattery?.charging,
            lastTransferName: transferStore.load().first?.fileName
        ))
        onChange?()
    }

    private func connectWiFiIfUseful() {
        guard isActive, pairedStore.load()?.endpoints.isEmpty == false, wifi.state == .idle else {
            return
        }
        Task {
            do {
                try await wifi.connectIfNeeded()
            } catch {
                log.info("Wi-Fi channel not available: \(String(describing: error))")
            }
        }
    }

    private func route(_ envelope: Fenpo_V1_Envelope, from channel: Channel) {
        for handler in extraHandlers where handler(envelope, channel) {
            return
        }
        if camera.handle(envelope) {
            return
        }
        if transfers.handle(envelope) {
            publishStatus()
            return
        }
        switch envelope.body {
        case .clipboard(let item):
            clipboard.handle(item)
        case .clipboardRequest:
            // iOS does not let a background app read the pasteboard; the iPhone sends its
            // clipboard on purpose instead (App Intents, controls).
            var error = Fenpo_V1_Error()
            error.code = .unsupported
            error.message = "The iPhone sends its clipboard on request from the phone only"
            reply(.error(error), to: envelope.id, on: channel)
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
        if !state.addresses.isEmpty, var record = pairedStore.load(), record.endpoints != state.addresses {
            record.endpoints = state.addresses
            try? pairedStore.update(record)
        }
        log.info("PC state: battery \(state.hasBatteryPercent ? "\(state.batteryPercent)%" : "unknown"), \(state.addresses.count) address(es)")
        publishStatus()
        connectWiFiIfUseful()
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
        let capabilities = (wifi.session ?? central.session).map { $0.capabilities.map { String(describing: $0) }.sorted() } ?? []
        return Diagnostics(
            bluetoothState: String(describing: central.state),
            wifiState: String(describing: wifi.state),
            peerName: central.session?.peerName ?? wifi.session?.peerName,
            capabilities: capabilities,
            pcName: record?.name,
            pcID: record?.pcID,
            bluetoothIdentifier: record?.bluetoothIdentifier?.uuidString,
            endpoints: record?.endpoints ?? [],
            counters: counters,
            deviceID: LocalDevice.deviceID,
            appVersion: LocalDevice.appVersion,
            build: Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "?"
        )
    }

    /// Snapshot for the Developer menu.
    public struct Diagnostics: Equatable, Sendable {
        public let bluetoothState: String
        public let wifiState: String
        public let peerName: String?
        public let capabilities: [String]
        public let pcName: String?
        public let pcID: String?
        public let bluetoothIdentifier: String?
        public let endpoints: [String]
        public let counters: BLEChannelCounters?
        public let deviceID: String
        public let appVersion: String
        public let build: String
    }
}
#endif
