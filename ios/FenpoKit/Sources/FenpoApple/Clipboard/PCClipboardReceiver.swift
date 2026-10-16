#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit

/// Puts clipboard items from the PC on the iPhone's pasteboard, also when the BLE channel wakes
/// the app in the background. Whether iOS allows that is what device test S6 measures, so the
/// result is logged in detail.
@MainActor
public final class PCClipboardReceiver {
    private static let lastItemKey = "clipboard.fromPC.date"

    private let log = FenpoLog("Clipboard")

    /// Creates the receiver.
    public init() {
    }

    /// Writes an item to the pasteboard.
    /// - Parameter item: Item from the PC.
    public func handle(_ item: Fenpo_V1_ClipboardItem) {
        let pasteboard = UIPasteboard.general
        let before = pasteboard.changeCount
        let kind: String
        switch item.content {
        case .text(let text):
            pasteboard.string = text
            kind = "text, \(text.count) characters"
        case .url(let text):
            if let url = URL(string: text) {
                pasteboard.url = url
            }
            kind = "url"
        case .image(let image):
            if let picture = UIImage(data: image.data) {
                pasteboard.image = picture
            }
            kind = "image, \(image.data.count) bytes"
        case nil:
            log.error("Clipboard item without content")
            return
        }
        let after = pasteboard.changeCount
        let state = UIApplication.shared.applicationState.logName
        let remaining = UIApplication.shared.backgroundTimeRemaining
        let budget = remaining > 1_000_000 ? "unlimited" : String(format: "%.1f s", remaining)
        log.info("Clipboard from PC: \(kind); app \(state); background time \(budget); changeCount \(before) -> \(after); hasStrings \(pasteboard.hasStrings)")
        UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.set(Date(), forKey: Self.lastItemKey)
    }
}

extension UIApplication.State {
    /// Short name for logs.
    public var logName: String {
        switch self {
        case .active:
            "active"
        case .inactive:
            "inactive"
        case .background:
            "background"
        @unknown default:
            "unknown"
        }
    }
}
#endif
