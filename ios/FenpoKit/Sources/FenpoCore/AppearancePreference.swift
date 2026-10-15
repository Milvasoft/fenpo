/// The appearance the user picked in Settings.
public enum AppearancePreference: String, CaseIterable, Sendable {
    /// Follow the system light or dark setting.
    case system

    /// Always light.
    case light

    /// Always dark.
    case dark

    /// The value used before the user picks anything.
    public static let defaultValue: AppearancePreference = .system

    /// Reads a stored raw value, falling back to the default for missing or unknown values.
    /// - Parameter rawValue: Value read from storage.
    public init(storedValue rawValue: String?) {
        self = rawValue.flatMap(AppearancePreference.init(rawValue:)) ?? .defaultValue
    }
}
