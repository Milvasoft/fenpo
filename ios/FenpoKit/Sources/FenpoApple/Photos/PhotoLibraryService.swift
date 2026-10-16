#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import Photos
import SwiftProtobuf
import UIKit
import UniformTypeIdentifiers

/// The iPhone's photo library browsed from the PC (protocol/README.md, "Photo library from the PC"):
/// pages of items newest first, thumbnails, and originals sent as one transfer with the PC's id.
/// Lists and thumbnails only go over Wi-Fi; on Bluetooth the answer is `BUSY`.
@MainActor
public final class PhotoLibraryService {
    /// Sends a reply on a channel (set by `FenpoServices`).
    public var reply: ((Fenpo_V1_Envelope.OneOf_Body, UInt64, FenpoServices.Channel) -> Void)?

    private let transfers: FileTransferService
    private let imageManager = PHCachingImageManager()
    private let log = FenpoLog("Photos")
    private static let pageLimit = 200
    private static let thumbnailLimit = 60
    private static let maxThumbnailPixels = 512

    /// Creates the service.
    /// - Parameter transfers: Sends the originals.
    public init(transfers: FileTransferService) {
        self.transfers = transfers
    }

    /// Handles photo requests; returns whether it took the message.
    /// - Parameters:
    ///   - envelope: Message from the PC.
    ///   - channel: Channel it came on.
    public func handle(_ envelope: Fenpo_V1_Envelope, from channel: FenpoServices.Channel) -> Bool {
        switch envelope.body {
        case .photoListRequest, .photoThumbnailRequest:
            guard channel == .wifi else {
                var error = Fenpo_V1_Error()
                error.code = .busy
                error.message = "Photos go over Wi-Fi only"
                reply?(.error(error), envelope.id, channel)
                return true
            }
            if case .photoListRequest(let request) = envelope.body {
                reply?(.photoList(list(request)), envelope.id, channel)
            } else if case .photoThumbnailRequest(let request) = envelope.body {
                Task {
                    await sendThumbnails(request, replyTo: envelope.id, on: channel)
                }
            }
            return true
        case .photoFetch(let fetch):
            Task {
                await send(fetch)
            }
            return true
        default:
            return false
        }
    }

    /// What the PC is told about access. Fenpo asks for it in the app; a request from the PC never
    /// shows a prompt.
    public static var access: Fenpo_V1_PhotoAccess {
        switch PHPhotoLibrary.authorizationStatus(for: .readWrite) {
        case .authorized:
            .full
        case .limited:
            .limited
        default:
            .denied
        }
    }

    /// Asks for photo access when it was never asked; for the PC's photo browser.
    public func requestAccessIfNeeded() async {
        guard PHPhotoLibrary.authorizationStatus(for: .readWrite) == .notDetermined else {
            return
        }
        _ = await PHPhotoLibrary.requestAuthorization(for: .readWrite)
    }

    private func list(_ request: Fenpo_V1_PhotoListRequest) -> Fenpo_V1_PhotoList {
        var list = Fenpo_V1_PhotoList()
        list.access = Self.access
        guard list.access != .denied else {
            log.info("Photo list asked, access denied")
            return list
        }
        let limit = min(Int(request.limit == 0 ? 100 : request.limit), Self.pageLimit)
        let options = PHFetchOptions()
        options.sortDescriptors = [NSSortDescriptor(key: "creationDate", ascending: false)]
        if request.hasBefore {
            options.predicate = NSPredicate(format: "creationDate < %@", request.before.date as NSDate)
        }
        options.fetchLimit = limit + 1
        let assets = PHAsset.fetchAssets(with: options)
        let count = min(assets.count, limit)
        list.hasMore = assets.count > limit
        list.items = (0..<count).map { index in
            Self.item(for: assets.object(at: index))
        }
        log.info("Photo list: \(count) item(s), more: \(list.hasMore)")
        return list
    }

    private static func item(for asset: PHAsset) -> Fenpo_V1_PhotoItem {
        var item = Fenpo_V1_PhotoItem()
        item.id = asset.localIdentifier
        if let date = asset.creationDate {
            item.takenAt = .init(date: date)
        }
        item.width = UInt32(asset.pixelWidth)
        item.height = UInt32(asset.pixelHeight)
        item.isVideo = asset.mediaType == .video
        if item.isVideo {
            item.durationMs = UInt32(max(0, asset.duration * 1000))
        }
        item.name = primaryResource(of: asset)?.originalFilename ?? ""
        return item
    }

    private static func primaryResource(of asset: PHAsset) -> PHAssetResource? {
        let resources = PHAssetResource.assetResources(for: asset)
        let wanted: [PHAssetResourceType] = asset.mediaType == .video ? [.fullSizeVideo, .video] : [.fullSizePhoto, .photo]
        for type in wanted {
            if let resource = resources.first(where: { $0.type == type }) {
                return resource
            }
        }
        return resources.first
    }

    private func sendThumbnails(_ request: Fenpo_V1_PhotoThumbnailRequest, replyTo: UInt64, on channel: FenpoServices.Channel) async {
        let ids = Array(request.ids.prefix(Self.thumbnailLimit))
        let side = CGFloat(min(Int(request.maxPx == 0 ? 256 : request.maxPx), Self.maxThumbnailPixels))
        let assets = PHAsset.fetchAssets(withLocalIdentifiers: ids, options: nil)
        var found: [String: PHAsset] = [:]
        assets.enumerateObjects { asset, _, _ in
            found[asset.localIdentifier] = asset
        }
        for id in ids {
            var thumbnail = Fenpo_V1_PhotoThumbnail()
            thumbnail.id = id
            if let asset = found[id], let jpeg = await jpeg(of: asset, side: side) {
                thumbnail.jpeg = jpeg
            }
            reply?(.photoThumbnail(thumbnail), replyTo, channel)
        }
    }

    private func jpeg(of asset: PHAsset, side: CGFloat) async -> Data? {
        let options = PHImageRequestOptions()
        options.deliveryMode = .highQualityFormat
        options.resizeMode = .fast
        options.isNetworkAccessAllowed = true
        let manager = imageManager
        let image: UIImage? = await withCheckedContinuation { continuation in
            manager.requestImage(for: asset, targetSize: CGSize(width: side, height: side), contentMode: .aspectFit, options: options) { image, info in
                // A degraded first image may come before the final one; answer once, with the final.
                if (info?[PHImageResultIsDegradedKey] as? Bool) == true {
                    return
                }
                continuation.resume(returning: image)
            }
        }
        return image?.jpegData(compressionQuality: 0.75)
    }

    /// Writes the originals to temporary files and offers them with the PC's transfer id.
    private func send(_ fetch: Fenpo_V1_PhotoFetch) async {
        let assets = PHAsset.fetchAssets(withLocalIdentifiers: fetch.ids, options: nil)
        var list: [PHAsset] = []
        assets.enumerateObjects { asset, _, _ in
            list.append(asset)
        }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("photo-fetch-\(fetch.transferID)", isDirectory: true)
        defer {
            try? FileManager.default.removeItem(at: folder)
        }
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            var files: [OutgoingFile] = []
            var taken: Set<String> = []
            for asset in list {
                guard let resource = Self.primaryResource(of: asset) else {
                    continue
                }
                let name = TransferPlan.safeFileName(resource.originalFilename, existing: taken)
                taken.insert(name)
                let url = folder.appendingPathComponent(name)
                try await Self.write(resource, to: url)
                let size = (try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.uint64Value ?? 0
                let type = UTType(resource.uniformTypeIdentifier)?.preferredMIMEType ?? TransferPlan.mimeType(forExtension: url.pathExtension)
                files.append(OutgoingFile(name: name, size: size, mimeType: type, url: url))
            }
            guard !files.isEmpty else {
                log.error("Photo fetch: none of \(fetch.ids.count) item(s) found")
                return
            }
            log.info("Photo fetch: sending \(files.count) original(s)")
            try await transfers.send(files, transferID: fetch.transferID)
        } catch {
            log.error("Photo fetch failed: \(String(describing: error))")
        }
    }

    private static func write(_ resource: PHAssetResource, to url: URL) async throws {
        let options = PHAssetResourceRequestOptions()
        options.isNetworkAccessAllowed = true
        try await PHAssetResourceManager.default().writeData(for: resource, toFile: url, options: options)
    }
}
#endif
