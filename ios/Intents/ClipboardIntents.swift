import AppIntents
import FenpoApple
import FenpoCore
import Foundation
import UIKit

// Compiled into both the app and the widget extension (controls need the intent in both). The work
// itself needs the app process, where `FenpoActionsRegistry` holds the BLE channel.

/// Sends text to the PC's clipboard. With "Get Clipboard" before it, a Shortcut sends the clipboard
/// without opening Fenpo (RESEARCH §5).
struct SendTextToPCIntent: AppIntent {
    static let title: LocalizedStringResource = "intent.sendText.title"
    static let description = IntentDescription("intent.sendText.description")

    @Parameter(title: "intent.sendText.parameter")
    var text: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await IntentSupport.actions(for: Self.self).sendText(text)
        return .result(dialog: "intent.sendText.done")
    }
}

/// Sends the iPhone's clipboard. iOS only lets the foreground app read the pasteboard, so this opens
/// Fenpo; for the Control Center, the Action button and Back Tap.
struct SendClipboardToPCIntent: AppIntent {
    static let title: LocalizedStringResource = "intent.sendClipboard.title"
    static let description = IntentDescription("intent.sendClipboard.description")
    static let openAppWhenRun = true
    static let authenticationPolicy = IntentAuthenticationPolicy.requiresAuthentication

    @MainActor
    func perform() async throws -> some IntentResult & ProvidesDialog {
        guard let text = UIPasteboard.general.string, !text.isEmpty else {
            throw FenpoActionError.emptyText
        }
        try await IntentSupport.actions(for: Self.self).sendText(text)
        return .result(dialog: "intent.sendText.done")
    }
}

/// Puts the PC's latest clipboard text on the iPhone and returns it, so a Shortcut can use it too.
struct PasteFromPCIntent: AppIntent {
    static let title: LocalizedStringResource = "intent.pasteFromPC.title"
    static let description = IntentDescription("intent.pasteFromPC.description")
    static let authenticationPolicy = IntentAuthenticationPolicy.requiresAuthentication

    @MainActor
    func perform() async throws -> some IntentResult & ReturnsValue<String> & ProvidesDialog {
        let text = try await IntentSupport.actions(for: Self.self).fetchPCClipboard()
        let pasteboard = UIPasteboard.general
        let before = pasteboard.changeCount
        pasteboard.string = text
        // UIApplication is unavailable in the widget extension, which compiles this file too.
        IntentSupport.log.info("Paste from PC wrote \(text.count) characters, changeCount \(before) -> \(pasteboard.changeCount)")
        return .result(value: text, dialog: "intent.pasteFromPC.done")
    }
}

/// Locks the PC's Windows session.
struct LockPCIntent: AppIntent {
    static let title: LocalizedStringResource = "intent.lockPC.title"
    static let description = IntentDescription("intent.lockPC.description")

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await IntentSupport.actions(for: Self.self).lockPC()
        return .result(dialog: "intent.lockPC.done")
    }
}

/// Shared helpers of the intents.
enum IntentSupport {
    static let log = FenpoLog("Intents")

    /// The app's actions; logs which process the intent ran in, an open question for device tests.
    /// - Parameter intent: Intent type, for the log.
    static func actions(for intent: Any.Type) throws -> any FenpoActions {
        let process = Bundle.main.bundlePath.hasSuffix(".appex") ? "widget extension" : "app"
        log.info("\(intent) runs in the \(process) process")
        return try FenpoActionsRegistry.shared.require()
    }
}

extension FenpoActionError: @retroactive CustomLocalizedStringResourceConvertible {
    /// User-facing message: what happened and how to fix it.
    public var localizedStringResource: LocalizedStringResource {
        switch self {
        case .notPaired:
            "action.error.notPaired"
        case .notConnected:
            "action.error.notConnected"
        case .tooLargeForBluetooth:
            "action.error.tooLarge"
        case .emptyText:
            "action.error.emptyText"
        case .nothingFromPC:
            "action.error.nothingFromPC"
        case .unavailableHere:
            "action.error.unavailableHere"
        case .unsupportedByPC:
            "action.error.unsupportedByPC"
        case .declinedByPC:
            "action.error.declinedByPC"
        case .transferFailed:
            "action.error.transferFailed"
        }
    }
}

/// Opens a link on the PC ("Open on PC"). In Shortcuts, "Show in Share Sheet" puts it in the share menu.
struct OpenOnPCIntent: AppIntent {
    static let title: LocalizedStringResource = "intent.openOnPC.title"
    static let description = IntentDescription("intent.openOnPC.description")

    @Parameter(title: "intent.openOnPC.parameter")
    var url: URL

    func perform() async throws -> some IntentResult & ProvidesDialog {
        try await IntentSupport.actions(for: Self.self).openOnPC(url)
        return .result(dialog: "intent.openOnPC.done")
    }
}
