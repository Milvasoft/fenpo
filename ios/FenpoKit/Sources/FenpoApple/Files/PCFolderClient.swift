#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UniformTypeIdentifiers

/// Why a request about the PC's shared folders did not work.
public enum PCFolderError: Error, Equatable, Sendable {
    /// No PC is paired.
    case notPaired

    /// The PC answered with an error (a path outside the shared folders, a missing item).
    case rejected(Fenpo_V1_ErrorCode)

    /// The PC answered with something else.
    case unexpectedReply
}

/// The PC's shared folders (protocol/README.md, "The PC's shared folders on the phone"), for the File
/// Provider extension: listings, thumbnails, changes, and contents as ordinary transfers. It has its
/// own Wi-Fi channel, next to the app's.
@MainActor
public final class PCFolderClient {
    /// Called with a folder's path when the PC says it changed.
    public var onFolderChanged: ((String) -> Void)?

    private let store: PairedPCStore
    private let wifi: WiFiChannel
    private let transfers: FileTransferService
    private let log = FenpoLog("Files")

    /// Creates a client with its own channel.
    public init() {
        store = PairedPCStore()
        wifi = WiFiChannel(client: DataChannelClient(store: store))
        transfers = FileTransferService(wifi: wifi)
        wifi.onEnvelope = { [weak self] envelope in
            self?.handle(envelope)
        }
    }

    /// Name of the paired PC, or `nil` without one.
    public var pcName: String? {
        store.load()?.name
    }

    /// What is in a folder; `""` lists the shared folders.
    /// - Parameter path: Folder path.
    public func list(_ path: String) async throws -> [Fenpo_V1_FolderEntry] {
        var request = Fenpo_V1_FolderListRequest()
        request.path = path
        guard case .folderListing(let listing) = try await ask(.folderListRequest(request)).body else {
            throw PCFolderError.unexpectedReply
        }
        return listing.entries
    }

    /// A small JPEG preview; empty when the PC has none.
    /// - Parameters:
    ///   - path: Item path.
    ///   - maxPixels: Longest side.
    public func thumbnail(_ path: String, maxPixels: Int) async throws -> Data {
        var request = Fenpo_V1_FileThumbnailRequest()
        request.path = path
        request.maxPx = UInt32(min(maxPixels, 512))
        guard case .fileThumbnail(let thumbnail) = try await ask(.fileThumbnailRequest(request)).body else {
            throw PCFolderError.unexpectedReply
        }
        return thumbnail.jpeg
    }

    /// Deletes (to the PC's Recycle Bin), renames, or creates a folder.
    /// - Parameters:
    ///   - kind: What to do.
    ///   - path: The item, or the parent folder for a new folder.
    ///   - newName: The new name, for a rename or a new folder.
    public func change(_ kind: Fenpo_V1_FileChangeKind, path: String, newName: String = "") async throws -> Fenpo_V1_FileChangeResult {
        var change = Fenpo_V1_FileChange()
        change.kind = kind
        change.path = path
        change.newName = newName
        guard case .fileChangeResult(let result) = try await ask(.fileChange(change)).body else {
            throw PCFolderError.unexpectedReply
        }
        return result
    }

    /// Downloads a file.
    /// - Parameters:
    ///   - path: File path on the PC.
    ///   - destination: Where it is written.
    public func download(_ path: String, to destination: URL) async throws {
        guard store.load() != nil else {
            throw PCFolderError.notPaired
        }
        log.info("Downloading a file")
        try await transfers.fetch(path: path, to: destination)
    }

    /// Uploads a file into a folder; a name that exists there gets " (2)".
    /// - Parameters:
    ///   - url: The file.
    ///   - name: Its name on the PC.
    ///   - folder: Folder path on the PC.
    public func upload(_ url: URL, name: String, into folder: String) async throws {
        guard store.load() != nil else {
            throw PCFolderError.notPaired
        }
        let size = (try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.uint64Value ?? 0
        let type = UTType(filenameExtension: (name as NSString).pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        log.info("Uploading a file, \(size) bytes")
        try await transfers.send([OutgoingFile(name: name, size: size, mimeType: type, url: url)], destinationPath: folder)
    }

    /// Closes the channel, for example when the extension goes away.
    public func disconnect() {
        wifi.disconnect()
    }

    private func ask(_ body: Fenpo_V1_Envelope.OneOf_Body) async throws -> Fenpo_V1_Envelope {
        guard store.load() != nil else {
            throw PCFolderError.notPaired
        }
        guard FeatureAccess.isAvailable(.filesApp) else {
            throw FenpoActionError.unavailableHere
        }
        try await wifi.connectIfNeeded()
        guard wifi.session?.supports(.fileProvider) == true else {
            throw FenpoActionError.unsupportedByPC
        }
        let reply = try await wifi.request(body)
        if case .error(let error) = reply.body {
            throw PCFolderError.rejected(error.code)
        }
        return reply
    }

    private func handle(_ envelope: Fenpo_V1_Envelope) {
        if transfers.handle(envelope) {
            return
        }
        if case .folderChanged(let changed) = envelope.body {
            onFolderChanged?(changed.path)
        }
    }
}
#endif
