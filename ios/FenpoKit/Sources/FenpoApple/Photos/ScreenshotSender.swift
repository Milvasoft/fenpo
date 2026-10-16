#if os(iOS)
import FenpoCore
import Foundation
import Photos
import UniformTypeIdentifiers

/// "Ekran görüntülerini gönder": screenshots taken since the last check go to the PC when Fenpo wakes
/// or the library changes while it runs, as Android does. Only screenshots newer than the moment the
/// switch was turned on, and each one once.
@MainActor
public final class ScreenshotSender: NSObject, PHPhotoLibraryChangeObserver {
    /// The Settings switch (App Group defaults); off until the user turns it on.
    public static let enabledKey = "feature.screenshots"
    private static let sinceKey = "screenshots.since"

    private let transfers: FileTransferService
    private let defaults = UserDefaults(suiteName: FenpoIdentifiers.appGroup)
    private var isSending = false
    private var isObserving = false
    private let log = FenpoLog("Screenshots")

    /// Creates the sender.
    /// - Parameter transfers: Sends the files.
    public init(transfers: FileTransferService) {
        self.transfers = transfers
    }

    /// Whether the user turned the feature on.
    public var isEnabled: Bool {
        defaults?.bool(forKey: Self.enabledKey) ?? false
    }

    /// The switch changed: from now on, new screenshots go to the PC.
    /// - Parameter isOn: New state.
    public func setEnabled(_ isOn: Bool) {
        defaults?.set(isOn, forKey: Self.enabledKey)
        if isOn {
            defaults?.set(Date().timeIntervalSince1970, forKey: Self.sinceKey)
            Task {
                if PHPhotoLibrary.authorizationStatus(for: .readWrite) == .notDetermined {
                    _ = await PHPhotoLibrary.requestAuthorization(for: .readWrite)
                }
                startObserving()
            }
        }
    }

    /// Fenpo woke: send what is new.
    public func check() {
        guard isEnabled else {
            return
        }
        startObserving()
        Task {
            await sendNew()
        }
    }

    public nonisolated func photoLibraryDidChange(_ changeInstance: PHChange) {
        Task { @MainActor in
            self.check()
        }
    }

    private func startObserving() {
        let status = PHPhotoLibrary.authorizationStatus(for: .readWrite)
        guard !isObserving, status == .authorized || status == .limited else {
            return
        }
        isObserving = true
        PHPhotoLibrary.shared().register(self)
    }

    private func sendNew() async {
        guard !isSending, let defaults else {
            return
        }
        let since = Date(timeIntervalSince1970: defaults.double(forKey: Self.sinceKey))
        let options = PHFetchOptions()
        options.predicate = NSPredicate(
            format: "(mediaSubtypes & %d) != 0 AND creationDate > %@",
            PHAssetMediaSubtype.photoScreenshot.rawValue,
            since as NSDate
        )
        options.sortDescriptors = [NSSortDescriptor(key: "creationDate", ascending: true)]
        let assets = PHAsset.fetchAssets(with: .image, options: options)
        guard assets.count > 0 else {
            return
        }
        isSending = true
        defer {
            isSending = false
        }
        var list: [PHAsset] = []
        assets.enumerateObjects { asset, _, _ in
            list.append(asset)
        }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("screenshots-\(UUID().uuidString)", isDirectory: true)
        defer {
            try? FileManager.default.removeItem(at: folder)
        }
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            var files: [OutgoingFile] = []
            var taken: Set<String> = []
            for asset in list {
                guard let resource = PHAssetResource.assetResources(for: asset).first(where: { $0.type == .photo }) else {
                    continue
                }
                let name = TransferPlan.safeFileName(resource.originalFilename, existing: taken)
                taken.insert(name)
                let url = folder.appendingPathComponent(name)
                let options = PHAssetResourceRequestOptions()
                options.isNetworkAccessAllowed = true
                try await PHAssetResourceManager.default().writeData(for: resource, toFile: url, options: options)
                let size = (try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.uint64Value ?? 0
                let type = UTType(resource.uniformTypeIdentifier)?.preferredMIMEType ?? "image/png"
                files.append(OutgoingFile(name: name, size: size, mimeType: type, url: url))
            }
            guard !files.isEmpty else {
                return
            }
            log.info("Sending \(files.count) screenshot(s)")
            try await transfers.send(files)
            if let newest = list.last?.creationDate {
                defaults.set(newest.timeIntervalSince1970, forKey: Self.sinceKey)
            }
        } catch {
            // Kept for the next wake: the "since" mark only moves after a successful send.
            log.error("Screenshots not sent: \(String(describing: error))")
        }
    }
}
#endif
