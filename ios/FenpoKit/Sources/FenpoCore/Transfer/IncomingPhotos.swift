import Foundation

/// "Gelen fotoğraf ve videoları kaydet": pictures and videos from the PC also go to the photo library. Settings live in
/// the App Group.
public enum IncomingPhotos {
    /// What a received file is in the photo library.
    public enum Kind: Equatable, Sendable {
        case photo
        case video
    }

    /// The Settings switch; off until the user turns it on.
    public static let enabledKey = "feature.savePhotos"

    private static let photoFormats: Set<String> = ["jpg", "jpeg", "png", "heic", "heif", "gif", "webp", "tif", "tiff", "bmp"]
    private static let videoFormats: Set<String> = ["mov", "mp4", "m4v"]

    /// Whether the user wants received pictures and videos in the photo library.
    /// - Parameter defaults: App Group defaults.
    public static func isEnabled(in defaults: UserDefaults?) -> Bool {
        defaults?.bool(forKey: enabledKey) ?? false
    }

    /// What a received file is, when the photo library can hold it.
    /// - Parameter fileName: Name of the file.
    /// - Returns: The kind, nil for anything else.
    public static func kind(ofFileName fileName: String) -> Kind? {
        guard let dot = fileName.lastIndex(of: ".") else {
            return nil
        }
        let format = fileName[fileName.index(after: dot)...].lowercased()
        if photoFormats.contains(format) {
            return .photo
        }
        return videoFormats.contains(format) ? .video : nil
    }
}
