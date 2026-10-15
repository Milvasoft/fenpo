/// Identifiers shared by the app, its extensions and the PC.
public enum FenpoIdentifiers {
    /// App Group that the app, the share extension and the widgets use for shared settings and state.
    public static let appGroup = "group.com.fenpo"

    /// Keychain access group (without the team prefix) for pairing keys and certificates.
    public static let keychainGroup = "com.fenpo.shared"

    /// DNS-SD service type the PC registers for the data channel.
    public static let bonjourServiceType = "_fenpo._tcp"
}
