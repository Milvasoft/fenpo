#if os(iOS)
import Foundation
import Photos

/// Adds received pictures to the photo library. Asks only for adding, not for reading the library.
public enum PhotoLibrarySaver {
    private static let log = FenpoLog("PhotoLibrarySaver")

    /// Asks for permission when it was never asked.
    /// - Returns: Whether pictures can be added.
    public static func requestAccess() async -> Bool {
        var status = PHPhotoLibrary.authorizationStatus(for: .addOnly)
        if status == .notDetermined {
            status = await PHPhotoLibrary.requestAuthorization(for: .addOnly)
        }
        return status == .authorized || status == .limited
    }

    /// Copies files into the photo library; the files stay where they are. A file that cannot be added is only logged:
    /// the transfer itself succeeded.
    /// - Parameter urls: Pictures to add.
    public static func save(_ urls: [URL]) async {
        let status = PHPhotoLibrary.authorizationStatus(for: .addOnly)
        guard !urls.isEmpty, status == .authorized || status == .limited else {
            return
        }
        for url in urls {
            do {
                try await PHPhotoLibrary.shared().performChanges {
                    let options = PHAssetResourceCreationOptions()
                    options.originalFilename = url.lastPathComponent
                    PHAssetCreationRequest.forAsset().addResource(with: .photo, fileURL: url, options: options)
                }
            } catch {
                log.error("Adding a received picture to the library failed: \(String(describing: error))")
            }
        }
    }
}
#endif
