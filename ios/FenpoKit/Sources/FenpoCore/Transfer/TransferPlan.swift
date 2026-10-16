import FenpoProtocol
import Foundation

/// A file the phone offers to the PC.
public struct OutgoingFile: Equatable, Sendable {
    /// Id inside the offer.
    public let fileID: String

    /// File name only, no path.
    public let name: String

    /// Size in bytes.
    public let size: UInt64

    /// MIME type.
    public let mimeType: String

    /// Where the bytes are read from.
    public let url: URL

    /// Creates a file entry.
    public init(fileID: String = UUID().uuidString, name: String, size: UInt64, mimeType: String, url: URL) {
        self.fileID = fileID
        self.name = name
        self.size = size
        self.mimeType = mimeType
        self.url = url
    }
}

/// File transfer pieces that do not depend on a platform (protocol/README.md, "File transfer").
public enum TransferPlan {
    /// Builds the offer for a set of files.
    /// - Parameters:
    ///   - transferID: Unique id of the transfer.
    ///   - files: Files in the order their bytes will be written.
    public static func offer(transferID: String, files: [OutgoingFile]) -> Fenpo_V1_TransferOffer {
        var offer = Fenpo_V1_TransferOffer()
        offer.transferID = transferID
        offer.files = files.map { file in
            var info = Fenpo_V1_FileInfo()
            info.fileID = file.fileID
            info.name = file.name
            info.size = file.size
            info.mimeType = file.mimeType
            return info
        }
        offer.totalBytes = files.reduce(0) { $0 + $1.size }
        return offer
    }

    /// First frame of a data connection: `[uint32 length][TransferStreamHeader]`.
    /// - Parameter transferID: Transfer the connection carries.
    public static func headerFrame(transferID: String) throws -> [UInt8] {
        var header = Fenpo_V1_TransferStreamHeader()
        header.transferID = transferID
        return try EnvelopeFraming.frame(try header.serializedBytes())
    }

    /// MIME type for a file extension, `application/octet-stream` when unknown.
    /// - Parameter fileExtension: Extension without the dot, any case.
    public static func mimeType(forExtension fileExtension: String) -> String {
        switch fileExtension.lowercased() {
        case "jpg", "jpeg": "image/jpeg"
        case "png": "image/png"
        case "heic": "image/heic"
        case "heif": "image/heif"
        case "gif": "image/gif"
        case "webp": "image/webp"
        case "mov": "video/quicktime"
        case "mp4", "m4v": "video/mp4"
        case "pdf": "application/pdf"
        case "txt", "md": "text/plain"
        case "zip": "application/zip"
        case "docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        case "xlsx": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        case "pptx": "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        case "mp3": "audio/mpeg"
        case "m4a": "audio/mp4"
        default: "application/octet-stream"
        }
    }

    /// A name that is safe to create in a folder: no path parts, no control characters, not hidden,
    /// and not taken by an existing file ("name (2).ext").
    /// - Parameters:
    ///   - proposed: Name from the other device.
    ///   - existing: Names already in the folder.
    public static func safeFileName(_ proposed: String, existing: Set<String>) -> String {
        let lastComponent = proposed
            .replacingOccurrences(of: "\\", with: "/")
            .split(separator: "/")
            .last
            .map(String.init) ?? ""
        var cleaned = String(lastComponent.unicodeScalars.filter { !CharacterSet.controlCharacters.contains($0) && $0 != ":" })
            .trimmingCharacters(in: .whitespacesAndNewlines)
        while cleaned.hasPrefix(".") {
            cleaned.removeFirst()
        }
        if cleaned.isEmpty {
            cleaned = "file"
        }

        guard existing.contains(cleaned) else {
            return cleaned
        }
        let base: String
        let suffix: String
        if let dot = cleaned.lastIndex(of: "."), dot != cleaned.startIndex {
            base = String(cleaned[..<dot])
            suffix = String(cleaned[dot...])
        } else {
            base = cleaned
            suffix = ""
        }
        var number = 2
        while existing.contains("\(base) (\(number))\(suffix)") {
            number += 1
        }
        return "\(base) (\(number))\(suffix)"
    }
}

/// Splits the bytes of a data connection into the offered files by their sizes.
public struct IncomingFileSplitter: Sendable {
    /// A piece of one file.
    public struct Piece: Equatable, Sendable {
        /// Index of the file in the offer.
        public let fileIndex: Int

        /// Bytes for that file.
        public let bytes: [UInt8]

        /// This piece completes the file.
        public let completesFile: Bool
    }

    private let sizes: [UInt64]
    private var fileIndex = 0
    private var writtenInFile: UInt64 = 0

    /// Bytes received for all files so far.
    public private(set) var bytesDone: UInt64 = 0

    /// Creates a splitter.
    /// - Parameter sizes: File sizes in offer order.
    public init(sizes: [UInt64]) {
        self.sizes = sizes
        skipEmptyFiles()
    }

    /// Every file has all its bytes.
    public var isComplete: Bool {
        fileIndex >= sizes.count
    }

    /// Index of the file the next byte belongs to (empty files are reported as completed first).
    public var currentFileIndex: Int {
        fileIndex
    }

    /// Files with size 0 that precede the current one have no bytes; callers create them up front.
    private mutating func skipEmptyFiles() {
        while fileIndex < sizes.count && sizes[fileIndex] == 0 {
            fileIndex += 1
        }
    }

    /// Splits received bytes. Bytes beyond the last file are ignored.
    /// - Parameter bytes: Newly received bytes.
    public mutating func consume(_ bytes: [UInt8]) -> [Piece] {
        var pieces: [Piece] = []
        var offset = 0
        while offset < bytes.count && fileIndex < sizes.count {
            let remaining = sizes[fileIndex] - writtenInFile
            let take = Int(min(UInt64(bytes.count - offset), remaining))
            writtenInFile += UInt64(take)
            bytesDone += UInt64(take)
            let completes = writtenInFile == sizes[fileIndex]
            pieces.append(Piece(fileIndex: fileIndex, bytes: Array(bytes[offset..<(offset + take)]), completesFile: completes))
            offset += take
            if completes {
                fileIndex += 1
                writtenInFile = 0
                skipEmptyFiles()
            }
        }
        return pieces
    }
}
