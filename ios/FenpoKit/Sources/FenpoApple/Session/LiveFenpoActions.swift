#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation

/// `FenpoActions` over the BLE channel. Text that does not fit BLE waits for the Wi-Fi data channel
/// (Windows side pending) and is reported as `tooLargeForBluetooth`.
@MainActor
public final class LiveFenpoActions: FenpoActions {
    private let store: PairedPCStore
    private let central: BLECentral
    private let log = FenpoLog("Actions")

    /// Creates the actions.
    public init(store: PairedPCStore, central: BLECentral) {
        self.store = store
        self.central = central
    }

    public nonisolated func sendText(_ text: String) async throws {
        try await performSendText(text)
    }

    public nonisolated func fetchPCClipboard() async throws -> String {
        try await performFetchPCClipboard()
    }

    public nonisolated func lockPC() async throws {
        try await performLockPC()
    }

    private func performSendText(_ text: String) async throws {
        guard !text.isEmpty else {
            throw FenpoActionError.emptyText
        }
        try await waitForChannel()
        var item = Fenpo_V1_ClipboardItem()
        item.itemID = UUID().uuidString
        item.text = text
        do {
            try central.send(.clipboard(item))
            log.info("Sent clipboard text, \(text.count) characters")
        } catch BLEChunking.Error.messageTooLarge {
            log.info("Clipboard text of \(text.count) characters does not fit BLE")
            throw FenpoActionError.tooLargeForBluetooth
        }
    }

    private func performFetchPCClipboard() async throws -> String {
        try await waitForChannel()
        let central = central
        let reply = try await withTimeout(.seconds(5)) {
            try await central.request(.clipboardRequest(Fenpo_V1_ClipboardRequest()))
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

    private func performLockPC() async throws {
        try await waitForChannel()
        try central.send(.lockPc(Fenpo_V1_LockPc()))
        log.info("Sent LockPc")
    }

    /// Gives a just-launched app a few seconds to reconnect before giving up.
    private func waitForChannel() async throws {
        guard store.load() != nil else {
            throw FenpoActionError.notPaired
        }
        guard central.state != .ready else {
            return
        }
        let central = central
        do {
            try await withTimeout(.seconds(8)) {
                try await central.waitUntilReady()
            }
        } catch {
            log.info("Channel not ready: \(String(describing: error))")
            throw FenpoActionError.notConnected
        }
    }
}
#endif
