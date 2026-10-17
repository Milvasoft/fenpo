import AppIntents
import FenpoCore
import Foundation

/// Fenpo's Focus filter (Settings › Focus › a Focus › Add Filter › Fenpo). With "Silence on PC" on,
/// the PC silences Fenpo notifications while the Focus lasts. iOS runs the filter again with the
/// default values when the Focus ends, which turns the silence off.
struct FenpoFocusFilter: SetFocusFilterIntent {
    static let title: LocalizedStringResource = "focus.filter.title"
    static let description = IntentDescription("focus.filter.description")

    @Parameter(title: "focus.filter.silencePC", default: false)
    var silencePC: Bool

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(title: silencePC ? "focus.filter.on" : "focus.filter.off")
    }

    @MainActor
    func perform() async throws -> some IntentResult {
        IntentSupport.log.info("Focus filter: silence \(silencePC)")
        guard FeatureAccess.isAvailable(.focusSync) else {
            return .result()
        }
        UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.set(silencePC, forKey: FocusSync.stateKey)
        do {
            try await IntentSupport.actions(for: Self.self).reportFocus(silencePC)
        } catch {
            // The state is kept and sent again when the PC is back; a Focus change must not fail.
            IntentSupport.log.info("Focus state not delivered now: \(String(describing: error))")
        }
        return .result()
    }
}
