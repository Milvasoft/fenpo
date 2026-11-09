import Foundation

/// "Gelen fotoğrafları kaydet": pictures from the PC also go to the photo library. Settings live in the App Group.
public enum IncomingPhotos {
    /// The Settings switch; off until the user turns it on.
    public static let enabledKey = "feature.savePhotos"

    private static let formats: Set<String> = ["jpg", "jpeg", "png", "heic", "heif", "gif", "webp", "tif", "tiff", "bmp"]

    /// Whether the user wants received pictures in the photo library.
    /// - Parameter defaults: App Group defaults.
    public static func isEnabled(in defaults: UserDefaults?) -> Bool {
        defaults?.bool(forKey: enabledKey) ?? false
    }

    /// Whether a received file is a picture the photo library can hold.
    /// - Parameter fileName: Name of the file.
    public static func isPhoto(fileName: String) -> Bool {
        guard let dot = fileName.lastIndex(of: ".") else {
            return false
        }
        return formats.contains(fileName[fileName.index(after: dot)...].lowercased())
    }
}
