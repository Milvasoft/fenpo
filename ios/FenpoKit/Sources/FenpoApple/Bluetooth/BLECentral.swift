#if os(iOS)
@preconcurrency import CoreBluetooth
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit

/// Client of the Fenpo BLE channel (path ③). The phone is the central, the PC the peripheral.
///
/// Uses state restoration and a pending `connect` that never times out, so iOS wakes the app when
/// the PC comes back or sends something (RESEARCH §6). Messages are sealed with a `ChannelCipher`
/// and split with `BLEChunking`. Lives on the main queue, created once at launch.
@MainActor
public final class BLECentral: NSObject {
    /// Channel state as the UI shows it.
    public enum State: Equatable, Sendable {
        /// Bluetooth is off on the phone.
        case bluetoothOff

        /// The user denied Bluetooth access.
        case unauthorized

        /// The device has no usable Bluetooth LE.
        case unsupported

        /// Nothing to connect to yet.
        case idle

        /// Waiting for the PC, connecting or setting up the characteristics.
        case connecting

        /// Notifications from the PC are on; messages can flow.
        case ready
    }

    /// Why a call cannot be served.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The channel is not ready.
        case notReady

        /// Bluetooth is off, denied or missing.
        case bluetoothUnavailable(State)
    }

    /// Restoration identifier; must stay the same across versions.
    public static let restoreIdentifier = "com.fenpo.central"

    private static let serviceUUID = CBUUID(string: BLEChannelIdentifiers.service)
    private static let rxUUID = CBUUID(string: BLEChannelIdentifiers.rx)
    private static let txUUID = CBUUID(string: BLEChannelIdentifiers.tx)

    /// Current state.
    public private(set) var state: State = .idle {
        didSet {
            guard state != oldValue else {
                return
            }
            log.info("BLE state \(String(describing: self.state))")
            onStateChange?(state)
            notifyChange()
        }
    }

    /// Latest `PcStatus` from the PC on this connection.
    public private(set) var pcStatus: Fenpo_V1_PcStatus? {
        didSet {
            notifyChange()
        }
    }

    /// What the PC agreed to in its `Hello` on this connection.
    public private(set) var session: NegotiatedSession? {
        didSet {
            notifyChange()
        }
    }

    /// Called on every state change.
    public var onStateChange: ((State) -> Void)?

    /// Called after the PC's `Hello` on this connection was accepted.
    public var onHello: (() -> Void)?

    /// Called for every Envelope from the PC that is not handled here (Hello, Ping).
    public var onEnvelope: ((Fenpo_V1_Envelope) -> Void)?

    private var manager: CBCentralManager!
    private var peripheral: CBPeripheral?
    private var targetIdentifier: UUID?
    private var rx: CBCharacteristic?
    private var tx: CBCharacteristic?

    private var cipher: (any ChannelCipher)?
    private var persistCounters: ((BLEChannelCounters) -> Void)?
    private var localHello: Fenpo_V1_Hello?
    private var sequencer = EnvelopeSequencer()
    private var reassembler = BLEReassembler()
    private var outbox: [[UInt8]] = []
    private var changeWaiters: [CheckedContinuation<Void, Never>] = []
    private var pendingReplies: [UInt64: CheckedContinuation<Fenpo_V1_Envelope, Swift.Error>] = [:]

    private let log = FenpoLog("BLE")

    /// Creates the central manager with state restoration. Create exactly one, at launch.
    override public init() {
        super.init()
        manager = CBCentralManager(delegate: self, queue: nil, options: [
            CBCentralManagerOptionRestoreIdentifierKey: Self.restoreIdentifier,
            CBCentralManagerOptionShowPowerAlertKey: false,
        ])
    }

    /// Sets the key, the `Hello` to send and where counters are saved. Call before `connect(to:)`.
    /// - Parameters:
    ///   - cipher: Channel cipher keyed with the pairing's BLE key.
    ///   - hello: This phone's `Hello`.
    ///   - persistCounters: Saves counters after every seal and open.
    public func configure(cipher: some ChannelCipher, hello: Fenpo_V1_Hello, persistCounters: @escaping (BLEChannelCounters) -> Void) {
        self.cipher = cipher
        self.localHello = hello
        self.persistCounters = persistCounters
    }

    /// Connects to the PC and keeps reconnecting until `disconnect()`.
    /// - Parameter identifier: Bluetooth identifier from AccessorySetupKit.
    public func connect(to identifier: UUID) {
        targetIdentifier = identifier
        connectTarget()
    }

    /// Stops connecting and forgets the PC.
    public func disconnect() {
        targetIdentifier = nil
        if let peripheral {
            manager.cancelPeripheralConnection(peripheral)
        }
        peripheral = nil
        resetLink()
        cipher = nil
        state = manager.state == .poweredOn ? .idle : state
    }

    /// Waits until the channel is ready. Cancel the task to stop waiting.
    public func waitUntilReady() async throws {
        while state != .ready {
            try Task.checkCancellation()
            switch state {
            case .unauthorized, .unsupported:
                throw Error.bluetoothUnavailable(state)
            default:
                await nextChange()
            }
        }
    }

    /// Waits until the PC reports that it is subscribed to ANCS (notification sharing allowed).
    public func waitForNotificationsSubscribed() async throws {
        while pcStatus?.notificationsSubscribed != true {
            try Task.checkCancellation()
            await nextChange()
        }
    }

    /// Waits for the PC's `Hello` on the current connection.
    public func waitForPeerHello() async throws -> NegotiatedSession {
        while true {
            if let session {
                return session
            }
            try Task.checkCancellation()
            await nextChange()
        }
    }

    /// Seals, chunks and writes a message to the PC.
    /// - Parameter body: Message to send.
    public func send(_ body: Fenpo_V1_Envelope.OneOf_Body) throws {
        try send(envelope: sequencer.envelope(body))
    }

    /// Sends an answer to a request from the PC.
    /// - Parameters:
    ///   - body: Answer.
    ///   - replyTo: Id of the request.
    public func send(_ body: Fenpo_V1_Envelope.OneOf_Body, replyTo: UInt64) throws {
        try send(envelope: sequencer.envelope(body, replyTo: replyTo))
    }

    /// Sends a request and waits for the Envelope whose `reply_to` matches it.
    /// Cancel the task (for example with `withTimeout`) to stop waiting.
    /// - Parameter body: Request to send.
    public func request(_ body: Fenpo_V1_Envelope.OneOf_Body) async throws -> Fenpo_V1_Envelope {
        let envelope = sequencer.envelope(body)
        let id = envelope.id
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                pendingReplies[id] = continuation
                do {
                    try send(envelope: envelope)
                } catch {
                    pendingReplies.removeValue(forKey: id)?.resume(throwing: error)
                }
            }
        } onCancel: {
            Task { @MainActor [weak self] in
                self?.pendingReplies.removeValue(forKey: id)?.resume(throwing: CancellationError())
            }
        }
    }

    // MARK: - Private

    private func connectTarget() {
        guard manager.state == .poweredOn, let targetIdentifier else {
            return
        }
        if peripheral?.identifier != targetIdentifier {
            peripheral = manager.retrievePeripherals(withIdentifiers: [targetIdentifier]).first
        }
        guard let peripheral else {
            log.error("Peripheral \(targetIdentifier) is not known to the system")
            state = .idle
            return
        }
        peripheral.delegate = self
        state = .connecting
        if peripheral.state == .connected {
            peripheral.discoverServices([Self.serviceUUID])
        } else {
            manager.connect(peripheral, options: nil)
        }
    }

    private func resetLink() {
        rx = nil
        tx = nil
        outbox.removeAll()
        reassembler = BLEReassembler()
        sequencer = EnvelopeSequencer()
        session = nil
        pcStatus = nil
        let replies = pendingReplies
        pendingReplies.removeAll()
        for reply in replies.values {
            reply.resume(throwing: Error.notReady)
        }
    }

    private func send(envelope: Fenpo_V1_Envelope) throws {
        guard state == .ready, var cipher, let peripheral, let rx else {
            throw Error.notReady
        }
        let sealed = try cipher.seal(try envelope.serializedBytes())
        self.cipher = cipher
        persistCounters?(cipher.counters)
        let length = peripheral.maximumWriteValueLength(for: .withoutResponse)
        outbox += try BLEChunking.chunks(of: sealed, maximumChunkLength: length)
        flush(peripheral: peripheral, characteristic: rx)
    }

    private func flush(peripheral: CBPeripheral, characteristic: CBCharacteristic) {
        while !outbox.isEmpty, peripheral.canSendWriteWithoutResponse {
            peripheral.writeValue(Data(outbox.removeFirst()), for: characteristic, type: .withoutResponse)
        }
    }

    private func receive(chunk: [UInt8]) {
        do {
            guard let message = try reassembler.append(chunk), var cipher else {
                return
            }
            let plaintext = try cipher.open(message)
            self.cipher = cipher
            persistCounters?(cipher.counters)
            handle(try Fenpo_V1_Envelope(serializedBytes: plaintext))
        } catch {
            log.error("Dropped BLE message: \(String(describing: error))")
        }
    }

    private func handle(_ envelope: Fenpo_V1_Envelope) {
        log.info("Received \(envelope.bodyName) #\(envelope.id), app \(UIApplication.shared.applicationState.logName)")
        if case .hello = envelope.body, let localHello {
            do {
                session = try Handshake.negotiate(local: localHello, remote: envelope)
                log.info("BLE Hello from \(self.session?.peerName ?? "")")
                onHello?()
            } catch {
                log.error("BLE handshake failed: \(String(describing: error))")
            }
            return
        }
        if envelope.replyTo != 0, let reply = pendingReplies.removeValue(forKey: envelope.replyTo) {
            reply.resume(returning: envelope)
            return
        }
        if case .pcStatus(let status) = envelope.body {
            log.info("PcStatus: notifications \(status.notificationsSubscribed), messages \(status.messagesAvailable)")
            pcStatus = status
            return
        }
        if let reply = Handshake.automaticReply(to: envelope, sequencer: &sequencer) {
            try? send(envelope: reply)
            return
        }
        onEnvelope?(envelope)
    }

    private func becameReady() {
        state = .ready
        if let localHello {
            do {
                try send(.hello(localHello))
            } catch {
                log.error("Sending Hello failed: \(String(describing: error))")
            }
        }
    }

    private func nextChange() async {
        await withTaskCancellationHandler {
            await withCheckedContinuation { continuation in
                changeWaiters.append(continuation)
            }
        } onCancel: {
            Task { @MainActor [weak self] in
                self?.notifyChange()
            }
        }
    }

    private func notifyChange() {
        let waiters = changeWaiters
        changeWaiters.removeAll()
        for waiter in waiters {
            waiter.resume()
        }
    }
}

extension BLECentral: @preconcurrency CBCentralManagerDelegate {
    public func centralManagerDidUpdateState(_ central: CBCentralManager) {
        switch central.state {
        case .poweredOn:
            if state == .bluetoothOff || state == .unauthorized || state == .unsupported {
                state = .idle
            }
            connectTarget()
        case .poweredOff, .resetting:
            resetLink()
            state = .bluetoothOff
        case .unauthorized:
            state = .unauthorized
        case .unsupported:
            state = .unsupported
        default:
            break
        }
    }

    public func centralManager(_ central: CBCentralManager, willRestoreState dict: [String: Any]) {
        let restored = dict[CBCentralManagerRestoredStatePeripheralsKey] as? [CBPeripheral] ?? []
        log.info("Restoring \(restored.count) peripheral(s)")
        if let first = restored.first {
            peripheral = first
            first.delegate = self
            targetIdentifier = first.identifier
        }
    }

    public func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        log.info("Connected to \(peripheral.identifier)")
        peripheral.discoverServices([Self.serviceUUID])
    }

    public func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: (any Swift.Error)?) {
        log.error("Connect failed: \(String(describing: error))")
        reconnect(peripheral)
    }

    public func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: (any Swift.Error)?) {
        log.info("Disconnected: \(String(describing: error))")
        reconnect(peripheral)
    }

    private func reconnect(_ peripheral: CBPeripheral) {
        resetLink()
        guard peripheral.identifier == targetIdentifier, manager.state == .poweredOn else {
            return
        }
        state = .connecting
        // A pending connect never times out; iOS completes it whenever the PC is in range again.
        manager.connect(peripheral, options: nil)
    }
}

extension BLECentral: @preconcurrency CBPeripheralDelegate {
    public func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: (any Swift.Error)?) {
        guard let service = peripheral.services?.first(where: { $0.uuid == Self.serviceUUID }) else {
            log.error("Fenpo service missing: \(String(describing: error))")
            return
        }
        peripheral.discoverCharacteristics([Self.rxUUID, Self.txUUID], for: service)
    }

    public func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: (any Swift.Error)?) {
        rx = service.characteristics?.first { $0.uuid == Self.rxUUID }
        tx = service.characteristics?.first { $0.uuid == Self.txUUID }
        guard let tx, rx != nil else {
            log.error("Fenpo characteristics missing: \(String(describing: error))")
            return
        }
        // tx requires encryption: the first subscription makes iOS pair (bond) with the PC.
        peripheral.setNotifyValue(true, for: tx)
    }

    public func peripheral(_ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic, error: (any Swift.Error)?) {
        guard characteristic.uuid == Self.txUUID else {
            return
        }
        if let error {
            log.error("Subscribing to tx failed: \(String(describing: error))")
            return
        }
        if characteristic.isNotifying {
            becameReady()
        }
    }

    public func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: (any Swift.Error)?) {
        guard characteristic.uuid == Self.txUUID, let value = characteristic.value else {
            return
        }
        receive(chunk: Array(value))
    }

    public func peripheralIsReady(toSendWriteWithoutResponse peripheral: CBPeripheral) {
        if let rx {
            flush(peripheral: peripheral, characteristic: rx)
        }
    }
}
#endif
