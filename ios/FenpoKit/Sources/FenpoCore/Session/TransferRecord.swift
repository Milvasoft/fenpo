import Foundation

/// A file that was sent to or received from the PC.
public struct TransferRecord: Equatable, Codable, Sendable, Identifiable {
    /// Which way the file went.
    public enum Direction: String, Equatable, Codable, Sendable {
        /// The PC sent it to the phone.
        case fromPC

        /// The phone sent it to the PC.
        case toPC
    }

    /// Unique id of the record.
    public let id: UUID

    /// File name without a path.
    public var fileName: String

    /// Which way the file went.
    public var direction: Direction

    /// When the transfer finished.
    public var date: Date

    /// Folder the file landed in on the PC, when known.
    public var folder: String?

    /// How the transfer ended.
    public var outcome: Outcome

    /// Size in bytes, when known.
    public var size: UInt64?

    /// Name of the received file in the app's Documents folder.
    public var localFileName: String?

    /// How a transfer ended.
    public enum Outcome: String, Equatable, Codable, Sendable {
        case completed
        case failed
        case cancelled
    }

    /// Creates a record.
    /// - Parameters:
    ///   - id: Unique id.
    ///   - fileName: File name without a path.
    ///   - direction: Which way the file went.
    ///   - date: When the transfer finished.
    ///   - folder: Folder the file landed in on the PC.
    ///   - outcome: How the transfer ended.
    ///   - size: Size in bytes.
    ///   - localFileName: Name of the received file in Documents.
    public init(
        id: UUID = UUID(),
        fileName: String,
        direction: Direction,
        date: Date,
        folder: String? = nil,
        outcome: Outcome = .completed,
        size: UInt64? = nil,
        localFileName: String? = nil
    ) {
        self.id = id
        self.fileName = fileName
        self.direction = direction
        self.date = date
        self.folder = folder
        self.outcome = outcome
        self.size = size
        self.localFileName = localFileName
    }

    /// Upper-cased extension such as "PDF", or an empty string.
    public var fileExtension: String {
        guard let dot = fileName.lastIndex(of: "."), dot != fileName.startIndex else {
            return ""
        }
        return String(fileName[fileName.index(after: dot)...]).uppercased()
    }

    /// Broad kind of the file, used for its badge.
    public var kind: FileKind {
        FileKind(fileExtension: fileExtension)
    }
}

/// Broad file categories that share a badge color.
public enum FileKind: Equatable, Sendable {
    case pdf
    case image
    case video
    case document
    case spreadsheet
    case other

    /// Classifies a file extension, ignoring case.
    /// - Parameter fileExtension: Extension without the dot.
    public init(fileExtension: String) {
        switch fileExtension.lowercased() {
        case "pdf":
            self = .pdf
        case "heic", "heif", "jpg", "jpeg", "png", "gif", "webp", "tif", "tiff", "dng":
            self = .image
        case "mov", "mp4", "m4v", "avi", "mkv":
            self = .video
        case "doc", "docx", "txt", "rtf", "pages", "md", "odt":
            self = .document
        case "xls", "xlsx", "csv", "numbers", "ods":
            self = .spreadsheet
        default:
            self = .other
        }
    }
}
