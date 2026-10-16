import Foundation

/// "Odak modunu eşitle": the iPhone tells the PC when a Focus with Fenpo's filter is on, and the PC
/// silences Fenpo notifications. Settings live in the App Group so the filter sees them.
public enum FocusSync {
    /// The Settings switch; on unless the user turned it off.
    public static let enabledKey = "feature.focus"

    /// Last state the filter reported, resent when the channel comes back.
    public static let stateKey = "focus.active"

    /// Whether the user wants the Focus shared with the PC.
    /// - Parameter defaults: App Group defaults.
    public static func isEnabled(in defaults: UserDefaults?) -> Bool {
        defaults?.object(forKey: enabledKey) as? Bool ?? true
    }

    /// What to tell the PC: the Focus is on only when the filter says so and the switch is on.
    /// - Parameters:
    ///   - filterActive: Value the Focus filter reported.
    ///   - enabled: The Settings switch.
    public static func reportedState(filterActive: Bool, enabled: Bool) -> Bool {
        filterActive && enabled
    }
}
