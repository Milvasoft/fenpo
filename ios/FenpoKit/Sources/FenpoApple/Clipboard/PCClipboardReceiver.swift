#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit
import UniformTypeIdentifiers

/// Puts clipboard items from the PC on the iPhone's pasteboard, also when the BLE channel wakes
/// the app in the background. Whether iOS keeps a write made in the background is what device test
/// S6 measures, so every write is checked and logged: an item that did not stick is written again
/// when Fenpo comes to the front, unless something else was copied on the iPhone meanwhile.
@MainActor
public final class PCClipboardReceiver {
    private static let lastItemKey = "clipboard.fromPC.date"
    /// Sensitive items leave the pasteboard after this long and never reach other devices.
    private static let sensitiveLifetime: TimeInterval = 120

    /// An item whose write did not stick, with the pasteboard it was meant to replace.
    private var pending: (item: Fenpo_V1_ClipboardItem, changeCount: Int)?
    /// Items from the PC since Fenpo was last in front: written, not stuck.
    private var backgroundCounts = (written: 0, notStuck: 0)

    private let log = FenpoLog("Clipboard")

    /// Creates the receiver.
    public init() {
    }

    /// Writes an item to the pasteboard.
    /// - Parameters:
    ///   - item: Item from the PC.
    ///   - channel: Channel it came on, for the log ("BLE" or "Wi-Fi").
    public func handle(_ item: Fenpo_V1_ClipboardItem, over channel: String) {
        let application = UIApplication.shared
        let state = application.applicationState
        let kind = Self.kind(of: item)
        guard ClipboardOnOpen.isSharingEnabled(in: UserDefaults(suiteName: FenpoIdentifiers.appGroup)) else {
            log.info("Clipboard from PC ignored (\(kind)): clipboard sharing is off")
            return
        }
        guard item.content != nil else {
            log.error("Clipboard item without content")
            return
        }
        // Keeps the process running until the pasteboard has the item, even if iOS woke Fenpo for it.
        let task = application.beginBackgroundTask(withName: "Fenpo clipboard from the PC")
        defer {
            if task != .invalid {
                application.endBackgroundTask(task)
            }
        }
        let remaining = application.backgroundTimeRemaining
        let budget = remaining > 1_000_000 ? "unlimited" : String(format: "%.1f s", remaining)
        let (before, after) = write(item)
        let stuck = after != before
        log.info("Clipboard from PC over \(channel): \(kind); app \(state.logName); background time \(budget); changeCount \(before) -> \(after); \(stuck ? "written" : "did not stick")")
        if stuck {
            pending = nil
            // The iPhone's clipboard is now the PC's: not something to offer back to the PC.
            PhoneClipboard.markHandled()
            UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.set(Date(), forKey: Self.lastItemKey)
        } else {
            pending = (item, before)
            log.error("Pasteboard write from the PC did not stick with the app \(state.logName); writing it again when Fenpo comes to the front")
        }
        if state != .active {
            if stuck {
                backgroundCounts.written += 1
            } else {
                backgroundCounts.notStuck += 1
            }
            // iOS may refuse or undo a background write later; look again once the write settled.
            Task {
                try? await Task.sleep(for: .seconds(2))
                let now = UIPasteboard.general.changeCount
                if stuck, now != after {
                    self.log.info("Pasteboard changed again after the background write (changeCount \(after) -> \(now))")
                }
            }
        }
    }

    /// Fenpo came to the front: reports what arrived in the background and writes an item that did
    /// not stick, if nothing else was copied on the iPhone since.
    public func appBecameActive() {
        if backgroundCounts.written + backgroundCounts.notStuck > 0 {
            log.info("While in the background: \(backgroundCounts.written) clipboard item(s) from the PC written, \(backgroundCounts.notStuck) did not stick")
        }
        backgroundCounts = (0, 0)
        guard let pending else {
            return
        }
        self.pending = nil
        let now = UIPasteboard.general.changeCount
        guard now == pending.changeCount else {
            log.info("Not writing the PC's item again: the iPhone's clipboard changed since (changeCount \(pending.changeCount) -> \(now))")
            return
        }
        let (before, after) = write(pending.item)
        log.info("Clipboard from PC written on return to the front: \(Self.kind(of: pending.item)); changeCount \(before) -> \(after)")
        if after != before {
            PhoneClipboard.markHandled()
        }
    }

    /// Writes the item with explicit types, so the pasteboard holds the bytes at once and needs
    /// nothing from Fenpo later (Fenpo may be suspended right after).
    private func write(_ item: Fenpo_V1_ClipboardItem) -> (before: Int, after: Int) {
        let pasteboard = UIPasteboard.general
        let before = pasteboard.changeCount
        var entry: [String: Any] = [:]
        switch item.content {
        case .text(let text):
            entry[UTType.utf8PlainText.identifier] = text
        case .url(let text):
            if let url = URL(string: text) {
                entry[UTType.url.identifier] = url
            }
            entry[UTType.utf8PlainText.identifier] = text
        case .image(let image):
            let type: UTType = image.mimeType == "image/jpeg" ? .jpeg : .png
            entry[type.identifier] = image.data
        case nil:
            return (before, before)
        }
        var options: [UIPasteboard.OptionsKey: Any] = [:]
        if item.sensitive {
            // Kept off other devices and out of clipboard managers, and gone after two minutes.
            entry["org.nspasteboard.ConcealedType"] = Data()
            options[.localOnly] = true
            options[.expirationDate] = Date().addingTimeInterval(Self.sensitiveLifetime)
        }
        pasteboard.setItems([entry], options: options)
        return (before, pasteboard.changeCount)
    }

    private static func kind(of item: Fenpo_V1_ClipboardItem) -> String {
        let kind = switch item.content {
        case .text(let text):
            "text, \(text.count) characters"
        case .url:
            "link"
        case .image(let image):
            "image, \(image.mimeType), \(image.data.count) bytes"
        case nil:
            "empty"
        }
        return item.sensitive ? "\(kind), sensitive" : kind
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
