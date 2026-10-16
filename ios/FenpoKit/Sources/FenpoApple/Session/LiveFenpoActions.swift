#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation

/// `FenpoActions` over the two channels: small messages go over BLE, which also works in the
/// background; anything larger, or anything while BLE is down, goes over the Wi-Fi channel.
@MainActor
public final class LiveFenpoActions: FenpoActions {
    private let store: PairedPCStore
    private let central: BLECentral
    private let wifi: WiFiChannel
    private let transfers: FileTransferService
    private let log = FenpoLog("Actions")

    /// Creates the actions.
    public init(store: PairedPCStore, central: BLECentral, wifi: WiFiChannel, transfers: FileTransferService) {
        self.store = store
        self.central = central
        self.wifi = wifi
        self.transfers = transfers
    }

    public nonisolated func sendText(_ text: String) async throws {
        try await performSendText(text)
    }

    public nonisolated func fetchPCClipboard() async throws -> String {
        try await performFetchPCClipboard()
    }

    public nonisolated func lockPC() async throws {
        try await deliver(.lockPc(Fenpo_V1_LockPc()), what: "LockPc")
    }

    public nonisolated func openOnPC(_ url: URL) async throws {
        var message = Fenpo_V1_OpenUrl()
        message.url = url.absoluteString
        try await deliver(.openURL(message), what: "OpenUrl")
    }

    public nonisolated func sendFiles(_ files: [OutgoingFile]) async throws {
        try await performSendFiles(files)
    }

    private func performSendFiles(_ files: [OutgoingFile]) async throws {
        guard store.load() != nil else {
            throw FenpoActionError.notPaired
        }
        try await transfers.send(files)
    }

    private func performSendText(_ text: String) async throws {
        guard !text.isEmpty else {
            throw FenpoActionError.emptyText
        }
        var item = Fenpo_V1_ClipboardItem()
        item.itemID = UUID().uuidString
        item.text = text
        try await deliver(.clipboard(item), what: "clipboard text, \(text.count) characters")
    }

    private func performFetchPCClipboard() async throws -> String {
        guard store.load() != nil else {
            throw FenpoActionError.notPaired
        }
        let reply: Fenpo_V1_Envelope
        if await bluetoothReady() {
            let central = central
            reply = try await withTimeout(.seconds(5)) {
                try await central.request(.clipboardRequest(Fenpo_V1_ClipboardRequest()))
            }
        } else {
            try await connectWiFi()
            let wifi = wifi
            let answer = Task {
                try await wifi.next(timeout: .seconds(5)) { envelope in
                    if case .clipboard = envelope.body {
                        return true
                    }
                    if case .error = envelope.body {
                        return true
                    }
                    return false
                }
            }
            try await wifi.send(.clipboardRequest(Fenpo_V1_ClipboardRequest()))
            reply = try await answer.value
        }
        switch reply.body {
        case .clipboard(let item):
            switch item.content {
            case .text(let text):
                return text
            case .url(let url):
                return url
            default:
                throw FenpoActionError.nothingFromPC
            }
        default:
            throw FenpoActionError.nothingFromPC
        }
    }

    /// BLE first; Wi-Fi when BLE is down or the message is too large for it.
    private func deliver(_ body: Fenpo_V1_Envelope.OneOf_Body, what: String) async throws {
        guard store.load() != nil else {
            throw FenpoActionError.notPaired
        }
        if await bluetoothReady() {
            do {
                try central.send(body)
                log.info("Sent \(what) over BLE")
                return
            } catch BLEChunking.Error.messageTooLarge {
                log.info("Too large for BLE, using Wi-Fi")
            }
        }
        try await connectWiFi()
        do {
            try await wifi.send(body)
            log.info("Sent \(what) over Wi-Fi")
        } catch {
            throw FenpoActionError.notConnected
        }
    }

    /// Gives a just-launched app a few seconds to reconnect BLE.
    private func bluetoothReady() async -> Bool {
        if central.state == .ready {
            return true
        }
        let central = central
        return (try? await withTimeout(.seconds(6)) {
            try await central.waitUntilReady()
        }) != nil
    }

    private func connectWiFi() async throws {
        do {
            try await wifi.connectIfNeeded()
        } catch {
            log.info("Wi-Fi channel not available: \(String(describing: error))")
            throw FenpoActionError.notConnected
        }
    }
}
#endif
