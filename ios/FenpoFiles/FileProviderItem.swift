import FenpoProtocol
import FileProvider
import Foundation
import UniformTypeIdentifiers

/// A file or folder of the PC's shared folders. The item identifier is its path on the PC; the root is
/// the list of shared folders ("").
final class FileProviderItem: NSObject, NSFileProviderItem {
    let itemIdentifier: NSFileProviderItemIdentifier
    let parentItemIdentifier: NSFileProviderItemIdentifier
    let filename: String
    let contentType: UTType
    let documentSize: NSNumber?
    let contentModificationDate: Date?
    let capabilities: NSFileProviderItemCapabilities
    let itemVersion: NSFileProviderItemVersion

    /// The PC's shared folders, shown under the PC's name.
    init(rootNamed name: String) {
        itemIdentifier = .rootContainer
        parentItemIdentifier = .rootContainer
        filename = name
        contentType = .folder
        documentSize = nil
        contentModificationDate = nil
        capabilities = [.allowsReading, .allowsContentEnumerating]
        itemVersion = NSFileProviderItemVersion(contentVersion: Data("root".utf8), metadataVersion: Data("root".utf8))
    }

    /// An entry of a listing.
    /// - Parameters:
    ///   - entry: What the PC sent.
    ///   - parentPath: Path of the folder it is in.
    init(entry: Fenpo_V1_FolderEntry, parentPath: String) {
        itemIdentifier = Self.identifier(for: entry.path)
        parentItemIdentifier = Self.identifier(for: parentPath)
        filename = entry.name
        if entry.isFolder {
            contentType = .folder
        } else {
            contentType = UTType(mimeType: entry.mimeType) ?? UTType(filenameExtension: (entry.name as NSString).pathExtension) ?? .data
        }
        documentSize = entry.isFolder ? nil : NSNumber(value: entry.size)
        let modified = entry.hasModifiedAt ? Date(timeIntervalSince1970: TimeInterval(entry.modifiedAt.seconds) + TimeInterval(entry.modifiedAt.nanos) / 1_000_000_000) : nil
        contentModificationDate = modified
        // A shared folder itself is the PC user's choice; it cannot be renamed or deleted from here.
        let isSharedFolder = parentPath.isEmpty
        var capabilities: NSFileProviderItemCapabilities = [.allowsReading]
        if entry.isFolder {
            capabilities.insert(.allowsContentEnumerating)
        }
        if !entry.readOnly {
            if entry.isFolder {
                capabilities.insert(.allowsAddingSubItems)
            }
            if !isSharedFolder {
                capabilities.formUnion([.allowsRenaming, .allowsDeleting])
            }
        }
        self.capabilities = capabilities
        let content = "\(entry.size)-\(modified?.timeIntervalSince1970 ?? 0)"
        itemVersion = NSFileProviderItemVersion(contentVersion: Data(content.utf8), metadataVersion: Data("\(content)-\(entry.name)".utf8))
    }

    /// The identifier of a path; "" is the root.
    static func identifier(for path: String) -> NSFileProviderItemIdentifier {
        path.isEmpty ? .rootContainer : NSFileProviderItemIdentifier(path)
    }

    /// The path of an identifier.
    static func path(of identifier: NSFileProviderItemIdentifier) -> String {
        identifier == .rootContainer ? "" : identifier.rawValue
    }

    /// The folder a path is in.
    static func parentPath(of path: String) -> String {
        guard let slash = path.lastIndex(of: "/") else {
            return ""
        }
        return String(path[..<slash])
    }
}
