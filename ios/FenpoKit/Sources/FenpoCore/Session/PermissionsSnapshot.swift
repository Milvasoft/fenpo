/// State of a system permission as shown in Settings.
public enum PermissionState: Equatable, Sendable {
    /// Allowed.
    case allowed

    /// Denied; the user has to change it in iOS Settings.
    case denied

    /// iOS asks every time (paste from other apps).
    case ask

    /// Not asked yet.
    case notDetermined
}

/// The permissions Fenpo depends on.
public struct PermissionsSnapshot: Equatable, Sendable {
    /// Bluetooth access.
    public var bluetooth: PermissionState

    /// Local network access.
    public var localNetwork: PermissionState

    /// Paste from other apps.
    public var paste: PermissionState

    /// Creates a snapshot.
    /// - Parameters:
    ///   - bluetooth: Bluetooth access.
    ///   - localNetwork: Local network access.
    ///   - paste: Paste from other apps.
    public init(bluetooth: PermissionState, localNetwork: PermissionState, paste: PermissionState) {
        self.bluetooth = bluetooth
        self.localNetwork = localNetwork
        self.paste = paste
    }

    /// The state shown in the design.
    public static let sample = PermissionsSnapshot(bluetooth: .allowed, localNetwork: .allowed, paste: .ask)
}
