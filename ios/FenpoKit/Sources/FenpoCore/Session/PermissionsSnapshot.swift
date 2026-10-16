/// State of a system permission as shown in Settings.
public enum PermissionState: Equatable, Sendable {
    /// Allowed.
    case allowed

    /// Denied; the user has to change it in iOS Settings.
    case denied

    /// Not asked yet.
    case notDetermined

    /// iOS does not tell apps (paste from other apps, local network before it was used).
    case unknown
}

/// The permissions Fenpo depends on.
public struct PermissionsSnapshot: Equatable, Sendable {
    /// Bluetooth access.
    public var bluetooth: PermissionState

    /// Local network access.
    public var localNetwork: PermissionState

    /// Paste from other apps.
    public var paste: PermissionState

    /// The photo library, for the PC's photo browser and sending screenshots.
    public var photos: PermissionState

    /// Creates a snapshot.
    /// - Parameters:
    ///   - bluetooth: Bluetooth access.
    ///   - localNetwork: Local network access.
    ///   - paste: Paste from other apps.
    ///   - photos: The photo library.
    public init(bluetooth: PermissionState, localNetwork: PermissionState, paste: PermissionState, photos: PermissionState = .allowed) {
        self.bluetooth = bluetooth
        self.localNetwork = localNetwork
        self.paste = paste
        self.photos = photos
    }

    /// The state shown in the design.
    public static let sample = PermissionsSnapshot(bluetooth: .allowed, localNetwork: .allowed, paste: .unknown)
}
