@preconcurrency import CoreBluetooth
import FenpoApple
import FenpoCore
import FenpoProtocol
import Foundation

/// GATT server with the fixed Fenpo service, `rx` and `tx`, sealing and chunking exactly like the PC.
@MainActor
final class TestPeripheralServer: NSObject {
    static let helpText = """
    Fenpo Test Peripheral. Commands:
      status                 Show Bluetooth, subscription and counters
      ping                   Send Ping (the phone answers Pong)
      ping-every <seconds>   Send Ping repeatedly (background wake test); `stop` ends it
      clip <text>            Send a clipboard text (S6)
      clip-in <seconds> <text>  Send a clipboard text after a delay, to lock or background the phone first
      ring                   Send RingDevice
      pc-clip <text>         Set the text the "PC clipboard" returns to Paste from PC
      notifications on|off   Change PcStatus.notifications_subscribed and send it
      stop                   Stop ping-every
      help                   This text
      quit                   Exit
    """

    private static let serviceUUID = CBUUID(string: BLEChannelIdentifiers.service)

    private var manager: CBPeripheralManager!
    private let rx = CBMutableCharacteristic(
        type: CBUUID(string: BLEChannelIdentifiers.rx),
        properties: [.writeWithoutResponse],
        value: nil,
        permissions: [.writeEncryptionRequired]
    )
    private let tx = CBMutableCharacteristic(
        type: CBUUID(string: BLEChannelIdentifiers.tx),
        properties: [.notifyEncryptionRequired],
        value: nil,
        permissions: [.readEncryptionRequired]
    )

    private var cipher: AESGCMChannelCipher
    private var reassembler = BLEReassembler()
    private var sequencer = EnvelopeSequencer()
    private var subscriber: CBCentral?
    private var outbox: [Data] = []
    private var notificationsSubscribed = true
    private var pcClipboard = "Fenpo test PC clipboard"
    private var repeatingPing: Task<Void, Never>?
    private let countersURL: URL

    override init() {
        let directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("FenpoTestPeripheral", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        countersURL = directory.appendingPathComponent("counters.json")
        let counters = (try? Data(contentsOf: countersURL)).flatMap { try? JSONDecoder().decode(BLEChannelCounters.self, from: $0) } ?? BLEChannelCounters()
        // The key is the fixed, public test key; it never protects anything real.
        cipher = try! AESGCMChannelCipher(key: TestPeripheral.bleChannelKey, sender: .pc, counters: counters)  // swiftlint:disable:this force_try
        super.init()
    }

    func start() {
        manager = CBPeripheralManager(delegate: self, queue: nil)
    }

    func handle(command line: String) {
        let parts = line.trimmingCharacters(in: .whitespaces).split(separator: " ", maxSplits: 2).map(String.init)
        guard let command = parts.first else {
            return
        }
        switch command {
        case "status":
            say("Bluetooth \(manager.state.rawValue), subscriber \(subscriber?.identifier.uuidString ?? "none"), counters sent \(cipher.counters.lastSent) received \(cipher.counters.lastReceived), notifications \(notificationsSubscribed)")
        case "ping":
            sendPing()
        case "ping-every":
            let seconds = Double(parts.dropFirst().first ?? "") ?? 30
            repeatingPing?.cancel()
            repeatingPing = Task {
                while !Task.isCancelled {
                    sendPing()
                    try? await Task.sleep(for: .seconds(seconds))
                }
            }
            say("Pinging every \(seconds) s; type `stop` to end")
        case "stop":
            repeatingPing?.cancel()
            repeatingPing = nil
            say("Stopped")
        case "clip":
            sendClipboard(parts.dropFirst().joined(separator: " "))
        case "clip-in":
            let rest = parts.dropFirst()
            let seconds = Double(rest.first ?? "") ?? 15
            let text = rest.dropFirst().joined(separator: " ")
            say("Sending clipboard in \(seconds) s")
            Task {
                try? await Task.sleep(for: .seconds(seconds))
                sendClipboard(text)
            }
        case "ring":
            send(.ring(Fenpo_V1_RingDevice()))
        case "pc-clip":
            pcClipboard = parts.dropFirst().joined(separator: " ")
            say("PC clipboard is now \"\(pcClipboard)\"")
        case "notifications":
            notificationsSubscribed = parts.dropFirst().first != "off"
            sendStatus()
        case "help":
            print(Self.helpText)
        case "quit", "exit":
            exit(0)
        default:
            say("Unknown command; type `help`")
        }
    }

    // MARK: - Channel

    private func sendPing() {
        var ping = Fenpo_V1_Ping()
        ping.nonce = UInt64.random(in: 1...UInt64.max)
        send(.ping(ping))
    }

    private func sendClipboard(_ text: String) {
        var item = Fenpo_V1_ClipboardItem()
        item.itemID = UUID().uuidString
        item.text = text.isEmpty ? "Fenpo test \(Date())" : text
        send(.clipboard(item))
    }

    private func sendStatus() {
        var status = Fenpo_V1_PcStatus()
        status.notificationsSubscribed = notificationsSubscribed
        status.messagesAvailable = false
        send(.pcStatus(status))
    }

    private func send(_ body: Fenpo_V1_Envelope.OneOf_Body) {
        send(envelope: sequencer.envelope(body))
    }

    private func send(envelope: Fenpo_V1_Envelope) {
        guard let subscriber else {
            say("Not sent: the phone is not subscribed")
            return
        }
        do {
            let sealed = try cipher.seal(try envelope.serializedBytes())
            saveCounters()
            let chunks = try BLEChunking.chunks(of: sealed, maximumChunkLength: subscriber.maximumUpdateValueLength)
            outbox += chunks.map { Data($0) }
            flush()
            say("→ \(envelope.bodyName) #\(envelope.id) (\(chunks.count) chunk(s))")
        } catch {
            say("Send failed: \(error)")
        }
    }

    private func flush() {
        while let next = outbox.first {
            guard manager.updateValue(next, for: tx, onSubscribedCentrals: nil) else {
                return
            }
            outbox.removeFirst()
        }
    }

    private func receive(_ chunk: [UInt8]) {
        do {
            guard let message = try reassembler.append(chunk) else {
                return
            }
            let envelope = try Fenpo_V1_Envelope(serializedBytes: try cipher.open(message))
            saveCounters()
            say("← \(envelope.bodyName) #\(envelope.id)")
            handle(envelope)
        } catch {
            say("Dropped message: \(error)")
        }
    }

    private func handle(_ envelope: Fenpo_V1_Envelope) {
        switch envelope.body {
        case .hello(let hello):
            say("   phone \(hello.deviceName), \(hello.osVersion), \(hello.model), app \(hello.appVersion), capabilities \(hello.capabilities.map { "\($0)" }.joined(separator: " "))")
            var reply = Fenpo_V1_Hello()
            reply.deviceID = TestPeripheral.pcID
            reply.deviceName = "Fenpo Test Mac"
            reply.platform = .windows
            reply.appVersion = "0.0.0-test"
            reply.protocolVersions = [FenpoProtocolVersion.current]
            reply.capabilities = hello.capabilities
            reply.osVersion = ProcessInfo.processInfo.operatingSystemVersionString
            send(.hello(reply))
            sendStatus()
        case .pong(let pong):
            say("   pong nonce \(pong.nonce)")
        case .clipboard(let item):
            // Test tool only: show what arrived, as the PC would put it on its clipboard.
            say("   clipboard from phone: \"\(item.text)\" (\(item.text.count) characters)")
        case .clipboardRequest:
            var item = Fenpo_V1_ClipboardItem()
            item.itemID = UUID().uuidString
            item.text = pcClipboard
            send(envelope: sequencer.envelope(.clipboard(item), replyTo: envelope.id))
        case .lockPc:
            say("   the phone asked to lock the PC")
        default:
            if let reply = Handshake.automaticReply(to: envelope, sequencer: &sequencer) {
                send(envelope: reply)
            }
        }
    }

    private func saveCounters() {
        if let data = try? JSONEncoder().encode(cipher.counters) {
            try? data.write(to: countersURL)
        }
    }

    private func say(_ text: String) {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss.SSS"
        print("[\(formatter.string(from: Date()))] \(text)")
    }
}

extension TestPeripheralServer: @preconcurrency CBPeripheralManagerDelegate {
    func peripheralManagerDidUpdateState(_ peripheral: CBPeripheralManager) {
        say("Bluetooth state \(peripheral.state.rawValue) (5 = on)")
        guard peripheral.state == .poweredOn else {
            return
        }
        let service = CBMutableService(type: Self.serviceUUID, primary: true)
        service.characteristics = [rx, tx]
        peripheral.removeAllServices()
        peripheral.add(service)
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didAdd service: CBService, error: (any Error)?) {
        if let error {
            say("Adding the service failed: \(error)")
            return
        }
        peripheral.startAdvertising([
            CBAdvertisementDataServiceUUIDsKey: [Self.serviceUUID],
            CBAdvertisementDataLocalNameKey: TestPeripheral.name,
        ])
    }

    func peripheralManagerDidStartAdvertising(_ peripheral: CBPeripheralManager, error: (any Error)?) {
        say(error.map { "Advertising failed: \($0)" } ?? "Advertising \(TestPeripheral.name) with the Fenpo service")
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral, didSubscribeTo characteristic: CBCharacteristic) {
        subscriber = central
        reassembler = BLEReassembler()
        sequencer = EnvelopeSequencer()
        say("Phone subscribed (\(central.identifier.uuidString)), max update \(central.maximumUpdateValueLength) bytes")
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral, didUnsubscribeFrom characteristic: CBCharacteristic) {
        subscriber = nil
        outbox.removeAll()
        say("Phone unsubscribed")
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didReceiveWrite requests: [CBATTRequest]) {
        for request in requests where request.characteristic.uuid == rx.uuid {
            if let value = request.value {
                receive(Array(value))
            }
        }
    }

    func peripheralManagerIsReady(toUpdateSubscribers peripheral: CBPeripheralManager) {
        flush()
    }
}
