import FenpoApple
import FenpoCore
import FenpoProtocol
import FileProvider
import Foundation
import UniformTypeIdentifiers

/// The PC's shared folders in the Files app (protocol/README.md, "The PC's shared folders on the
/// phone"). Nothing is kept offline: listings and files come from the PC over Wi-Fi when asked for.
final class FileProviderExtension: NSObject, NSFileProviderReplicatedExtension, NSFileProviderThumbnailing {
    private let domain: NSFileProviderDomain

    required init(domain: NSFileProviderDomain) {
        self.domain = domain
        super.init()
        let manager = Unchecked(NSFileProviderManager(for: domain))
        Task { @MainActor in
            FilesSession.shared.watch { identifier in
                manager.value?.signalEnumerator(for: identifier) { _ in
                }
            }
        }
    }

    func invalidate() {
        Task { @MainActor in
            FilesSession.shared.client.disconnect()
        }
    }

    func item(for identifier: NSFileProviderItemIdentifier, request: NSFileProviderRequest, completionHandler: @escaping (NSFileProviderItem?, (any Error)?) -> Void) -> Progress {
        let completion = Unchecked(completionHandler)
        Task { @MainActor in
            do {
                completion.value(try await FilesSession.shared.item(for: identifier), nil)
            } catch {
                completion.value(nil, FilesSession.providerError(error))
            }
        }
        return Progress(totalUnitCount: 1)
    }

    func fetchContents(for itemIdentifier: NSFileProviderItemIdentifier, version requestedVersion: NSFileProviderItemVersion?, request: NSFileProviderRequest, completionHandler: @escaping (URL?, NSFileProviderItem?, (any Error)?) -> Void) -> Progress {
        let completion = Unchecked(completionHandler)
        let progress = Progress(totalUnitCount: 1)
        let manager = Unchecked(NSFileProviderManager(for: domain))
        Task { @MainActor in
            do {
                let item = try await FilesSession.shared.item(for: itemIdentifier)
                let folder = try manager.value?.temporaryDirectoryURL() ?? FileManager.default.temporaryDirectory
                let destination = folder.appendingPathComponent(UUID().uuidString).appendingPathExtension((item.filename as NSString).pathExtension)
                try await FilesSession.shared.client.download(FileProviderItem.path(of: itemIdentifier), to: destination)
                progress.completedUnitCount = 1
                completion.value(destination, item, nil)
            } catch {
                completion.value(nil, nil, FilesSession.providerError(error))
            }
        }
        return progress
    }

    func createItem(basedOn itemTemplate: NSFileProviderItem, fields: NSFileProviderItemFields, contents url: URL?, options: NSFileProviderCreateItemOptions = [], request: NSFileProviderRequest, completionHandler: @escaping (NSFileProviderItem?, NSFileProviderItemFields, Bool, (any Error)?) -> Void) -> Progress {
        let completion = Unchecked(completionHandler)
        let parentPath = FileProviderItem.path(of: itemTemplate.parentItemIdentifier)
        let name = itemTemplate.filename
        let isFolder = itemTemplate.contentType == .folder
        let contents = url
        Task { @MainActor in
            do {
                let client = FilesSession.shared.client
                if isFolder {
                    let result = try await client.change(.createFolder, path: parentPath, newName: name)
                    guard result.ok else {
                        throw PCFolderError.rejected(.invalidRequest)
                    }
                    completion.value(FileProviderItem(entry: result.entry, parentPath: parentPath), [], false, nil)
                } else {
                    guard let contents else {
                        throw CocoaError(.fileReadNoSuchFile)
                    }
                    try await client.upload(contents, name: name, into: parentPath)
                    // The PC may have added " (2)"; the newest item with that start is the upload.
                    let stem = (name as NSString).deletingPathExtension
                    let item = try await FilesSession.shared.items(in: parentPath)
                        .filter { $0.filename.hasPrefix(stem) }
                        .max { ($0.contentModificationDate ?? .distantPast) < ($1.contentModificationDate ?? .distantPast) }
                    completion.value(item, [], false, nil)
                }
            } catch {
                completion.value(nil, [], false, FilesSession.providerError(error))
            }
        }
        return Progress(totalUnitCount: 1)
    }

    func modifyItem(_ item: NSFileProviderItem, baseVersion version: NSFileProviderItemVersion, changedFields: NSFileProviderItemFields, contents newContents: URL?, options: NSFileProviderModifyItemOptions = [], request: NSFileProviderRequest, completionHandler: @escaping (NSFileProviderItem?, NSFileProviderItemFields, Bool, (any Error)?) -> Void) -> Progress {
        let completion = Unchecked(completionHandler)
        let identifier = item.itemIdentifier
        let path = FileProviderItem.path(of: identifier)
        let newName = item.filename
        // Renaming is the one change the PC takes; moving and editing contents in place are not.
        guard changedFields.subtracting([.filename, .lastUsedDate, .tagData, .favoriteRank]).isEmpty else {
            completionHandler(nil, [], false, NSError(domain: NSCocoaErrorDomain, code: CocoaError.featureUnsupported.rawValue))
            return Progress(totalUnitCount: 1)
        }
        Task { @MainActor in
            do {
                if changedFields.contains(.filename) {
                    let result = try await FilesSession.shared.client.change(.rename, path: path, newName: newName)
                    guard result.ok else {
                        throw PCFolderError.rejected(.invalidRequest)
                    }
                    completion.value(FileProviderItem(entry: result.entry, parentPath: FileProviderItem.parentPath(of: path)), [], false, nil)
                } else {
                    completion.value(try await FilesSession.shared.item(for: identifier), [], false, nil)
                }
            } catch {
                completion.value(nil, [], false, FilesSession.providerError(error))
            }
        }
        return Progress(totalUnitCount: 1)
    }

    func deleteItem(identifier: NSFileProviderItemIdentifier, baseVersion version: NSFileProviderItemVersion, options: NSFileProviderDeleteItemOptions = [], request: NSFileProviderRequest, completionHandler: @escaping ((any Error)?) -> Void) -> Progress {
        let completion = Unchecked(completionHandler)
        let path = FileProviderItem.path(of: identifier)
        Task { @MainActor in
            do {
                let result = try await FilesSession.shared.client.change(.delete, path: path)
                completion.value(result.ok ? nil : NSFileProviderError(.noSuchItem))
            } catch {
                completion.value(FilesSession.providerError(error))
            }
        }
        return Progress(totalUnitCount: 1)
    }

    func enumerator(for containerItemIdentifier: NSFileProviderItemIdentifier, request: NSFileProviderRequest) throws -> any NSFileProviderEnumerator {
        switch containerItemIdentifier {
        case .workingSet:
            return EmptyEnumerator()
        case .trashContainer:
            throw NSFileProviderError(.noSuchItem)
        default:
            return FolderEnumerator(path: FileProviderItem.path(of: containerItemIdentifier))
        }
    }

    func fetchThumbnails(for itemIdentifiers: [NSFileProviderItemIdentifier], requestedSize size: CGSize, perThumbnailCompletionHandler: @escaping (NSFileProviderItemIdentifier, Data?, (any Error)?) -> Void, completionHandler: @escaping ((any Error)?) -> Void) -> Progress {
        let each = Unchecked(perThumbnailCompletionHandler)
        let done = Unchecked(completionHandler)
        let progress = Progress(totalUnitCount: Int64(itemIdentifiers.count))
        let side = Int(max(size.width, size.height))
        Task { @MainActor in
            for identifier in itemIdentifiers {
                do {
                    let jpeg = try await FilesSession.shared.client.thumbnail(FileProviderItem.path(of: identifier), maxPixels: side)
                    each.value(identifier, jpeg.isEmpty ? nil : jpeg, nil)
                } catch {
                    each.value(identifier, nil, FilesSession.providerError(error))
                }
                progress.completedUnitCount += 1
            }
            done.value(nil)
        }
        return progress
    }
}

/// The extension's one connection to the PC and what it learned about items.
@MainActor
final class FilesSession {
    static let shared = FilesSession()

    let client = PCFolderClient()

    /// Listens for the PC's "folder changed" and asks the system to enumerate that folder again.
    /// - Parameter signal: Signals the enumerator of an item.
    func watch(signal: @escaping (NSFileProviderItemIdentifier) -> Void) {
        client.onFolderChanged = { path in
            signal(FileProviderItem.identifier(for: path))
        }
    }

    /// The items of a folder.
    func items(in path: String) async throws -> [FileProviderItem] {
        try await client.list(path).map { FileProviderItem(entry: $0, parentPath: path) }
    }

    /// One item, found in its folder's listing.
    func item(for identifier: NSFileProviderItemIdentifier) async throws -> FileProviderItem {
        if identifier == .rootContainer {
            return FileProviderItem(rootNamed: client.pcName ?? "PC")
        }
        let path = FileProviderItem.path(of: identifier)
        guard let item = try await items(in: FileProviderItem.parentPath(of: path)).first(where: { $0.itemIdentifier == identifier }) else {
            throw NSFileProviderError(.noSuchItem)
        }
        return item
    }

    /// The File Provider error that tells the Files app what went wrong.
    nonisolated static func providerError(_ error: any Error) -> any Error {
        if let error = error as? PCFolderError {
            switch error {
            case .notPaired:
                return NSFileProviderError(.notAuthenticated)
            case .rejected, .unexpectedReply:
                return NSFileProviderError(.noSuchItem)
            }
        }
        if error is NSFileProviderError || error is CocoaError {
            return error
        }
        return NSFileProviderError(.serverUnreachable)
    }
}
