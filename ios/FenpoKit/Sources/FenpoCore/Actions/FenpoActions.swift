import Foundation
import Synchronization

/// What App Intents, controls and the home screen ask of the PC.
///
/// Only the app process can talk to the PC (the BLE channel lives there), so the app registers its
/// implementation in `FenpoActionsRegistry` at launch. Anything that runs elsewhere, such as a widget
/// extension, finds no registration and reports `FenpoActionError.unavailableHere`.
public protocol FenpoActions: Sendable {
    /// Sends text to the PC's clipboard.
    /// - Parameter text: Text to send.
    func sendText(_ text: String) async throws

    /// Puts text, a link or an image on the PC's clipboard.
    /// - Parameter payload: What the iPhone's clipboard held.
    func sendClipboard(_ payload: ClipboardPayload) async throws

    /// Asks the PC for its latest clipboard text.
    func fetchPCClipboard() async throws -> String

    /// Asks the PC to lock its Windows session.
    func lockPC() async throws

    /// Opens a link in the PC's default browser or app ("Open on PC").
    /// - Parameter url: Link to open.
    func openOnPC(_ url: URL) async throws

    /// Sends files to the PC over the Wi-Fi data channel.
    /// - Parameter files: Files in the order to send.
    func sendFiles(_ files: [OutgoingFile]) async throws

    /// Tells the PC whether a Focus is on, so it can silence Fenpo notifications.
    /// - Parameter isActive: The Focus filter's state; the Settings switch is applied on top.
    func reportFocus(_ isActive: Bool) async throws

    /// A presentation remote step, for Shortcuts and the Action button.
    /// - Parameter command: Next or previous slide.
    func presenter(_ command: PresenterCommand) async throws

    /// Controls what plays on the PC.
    /// - Parameter command: Play/pause, next, previous or a volume.
    func pcMedia(_ command: PCMediaCommand) async throws
}

/// What the presentation remote intents send.
public enum PresenterCommand: Sendable {
    /// The next slide.
    case next

    /// The previous slide.
    case previous
}

/// Why an action could not be done. Each case has a user-facing message in the app.
public enum FenpoActionError: Error, Equatable, Sendable {
    /// No PC is paired.
    case notPaired

    /// The PC is paired but not reachable.
    case notConnected

    /// The text does not fit the BLE channel; large content waits for the Wi-Fi data channel.
    case tooLargeForBluetooth

    /// There is no text to send.
    case emptyText

    /// The PC answered without text.
    case nothingFromPC

    /// This process cannot reach the PC (for example a widget extension).
    case unavailableHere

    /// The PC's Fenpo does not offer this feature yet.
    case unsupportedByPC

    /// The PC declined, for example its user said no.
    case declinedByPC

    /// The transfer stopped before every byte arrived.
    case transferFailed

    /// Clipboard sharing is off in Fenpo's Settings.
    case sharingOff
}

/// Holds the app's `FenpoActions` for intents that run in the app process.
public final class FenpoActionsRegistry: Sendable {
    /// The process-wide registry.
    public static let shared = FenpoActionsRegistry()

    private let actions = Mutex<(any FenpoActions)?>(nil)

    private init() {
    }

    /// Registers the implementation; call at app launch.
    /// - Parameter actions: The app's actions.
    public func register(_ actions: any FenpoActions) {
        self.actions.withLock { $0 = actions }
    }

    /// The registered actions, or `nil` outside the app process.
    public var current: (any FenpoActions)? {
        actions.withLock { $0 }
    }

    /// The registered actions.
    public func require() throws(FenpoActionError) -> any FenpoActions {
        guard let current else {
            throw .unavailableHere
        }
        return current
    }
}
