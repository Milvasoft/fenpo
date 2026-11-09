#if os(iOS)
import FenpoCore
import Foundation
import Photos

/// Adds received pictures and videos to the photo library. Asks only for adding, not for reading the library.
public enum PhotoLibrarySaver {
    private static let log = FenpoLog("PhotoLibrarySaver")

    /// Asks for permission when it was never asked.
    /// - Returns: Whether files can be added.
    public static func requestAccess() async -> Bool {
        var status = PHPhotoLibrary.authorizationStatus(for: .addOnly)
        if status == .notDetermined {
            status = await PHPhotoLibrary.requestAuthorization(for: .addOnly)
        }
        return status == .authorized || status == .limited
    }

    /// Copies the pictures and videos among the files into the photo library; the files stay where they are. A file that
    /// cannot be added is only logged: the transfer itself succeeded.
    /// - Parameter urls: Received files; anything else is skipped.
    public static func save(_ urls: [URL]) async {
        let status = PHPhotoLibrary.authorizationStatus(for: .addOnly)
        guard status == .authorized || status == .limited else {
            return
        }
        for url in urls {
            guard let kind = IncomingPhotos.kind(ofFileName: url.lastPathComponent) else {
                continue
            }
            do {
                try await PHPhotoLibrary.shared().performChanges {
                    let options = PHAssetResourceCreationOptions()
                    options.originalFilename = url.lastPathComponent
                    PHAssetCreationRequest.forAsset().addResource(with: kind == .video ? .video : .photo, fileURL: url, options: options)
                }
            } catch {
                log.error("Adding a received file to the library failed: \(String(describing: error))")
            }
        }
    }
}
#endif
