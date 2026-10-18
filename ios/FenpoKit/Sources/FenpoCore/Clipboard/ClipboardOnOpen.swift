import Foundation

/// "Açılınca panoyu gönder": what Fenpo does with a clipboard that changed while it was away. iOS lets
/// only the app in front read the pasteboard, so opening Fenpo is the moment to send it. Settings and
/// the last handled pasteboard live in the App Group so intents and controls see them too.
public enum ClipboardOnOpen {
    /// What to do when Fenpo comes to the front with a new clipboard.
    public enum Mode: String, CaseIterable, Sendable {
        /// Show a card with the system Paste button: one tap, never a paste prompt.
        case ask

        /// Read and send at once; iOS asks for permission each time unless "Paste from Other Apps"
        /// is set to Allow for Fenpo.
        case automatic

        /// Do nothing.
        case off
    }

    /// What `decide` concluded.
    public enum Decision: Equatable, Sendable {
        /// Nothing new, or nothing to do with it.
        case nothing

        /// Offer it with the Paste button.
        case offer

        /// Send it now.
        case send
    }

    /// The Settings choice.
    public static let modeKey = "clipboard.onOpen"

    /// `UIPasteboard.changeCount` of the last clipboard Fenpo sent, offered, dismissed or wrote from the PC.
    public static let handledChangeCountKey = "clipboard.handledChangeCount"

    /// The Settings switch "Pano paylaşımı"; on unless the user turned it off.
    public static let sharingKey = "feature.clipboard"

    /// The user's choice; asks unless changed.
    /// - Parameter defaults: App Group defaults.
    public static func mode(in defaults: UserDefaults?) -> Mode {
        defaults?.string(forKey: modeKey).flatMap(Mode.init(rawValue:)) ?? .ask
    }

    /// Whether clipboard sharing is on.
    /// - Parameter defaults: App Group defaults.
    public static func isSharingEnabled(in defaults: UserDefaults?) -> Bool {
        defaults?.object(forKey: sharingKey) as? Bool ?? true
    }

    /// The pasteboard Fenpo last dealt with, or `nil` before the first time.
    /// - Parameter defaults: App Group defaults.
    public static func handledChangeCount(in defaults: UserDefaults?) -> Int? {
        defaults?.object(forKey: handledChangeCountKey) as? Int
    }

    /// Remembers that Fenpo dealt with this pasteboard, so it is not offered again.
    /// - Parameters:
    ///   - changeCount: `UIPasteboard.changeCount` now.
    ///   - defaults: App Group defaults.
    public static func markHandled(_ changeCount: Int, in defaults: UserDefaults?) {
        defaults?.set(changeCount, forKey: handledChangeCountKey)
    }

    /// Decides what to do when Fenpo comes to the front. Uses pasteboard metadata only, which iOS
    /// gives without a paste prompt.
    /// - Parameters:
    ///   - changeCount: `UIPasteboard.changeCount` now.
    ///   - handled: The pasteboard Fenpo last dealt with; `nil` the first time, which only records it.
    ///   - hasContent: The pasteboard has text, a link or an image.
    ///   - mightBeSensitive: Marked private, or the system detects a number that may be a code.
    ///   - isConnected: The PC is reachable.
    ///   - sharingEnabled: The clipboard sharing switch.
    ///   - mode: The Settings choice.
    public static func decide(changeCount: Int, handled: Int?, hasContent: Bool, mightBeSensitive: Bool, isConnected: Bool, sharingEnabled: Bool, mode: Mode) -> Decision {
        guard let handled, handled != changeCount, hasContent, isConnected, sharingEnabled else {
            return .nothing
        }
        switch mode {
        case .off:
            return .nothing
        case .ask:
            return .offer
        case .automatic:
            // A password or a code goes only with the user's tap.
            return mightBeSensitive ? .offer : .send
        }
    }
}
