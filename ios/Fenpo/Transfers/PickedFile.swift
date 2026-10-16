import CoreTransferable
import FenpoCore
import Foundation
import UniformTypeIdentifiers

/// A photo or video from the picker, copied to a temporary file so it can be streamed to the PC.
struct PickedFile: Transferable {
    let url: URL

    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(importedContentType: .item) { received in
            let folder = FileManager.default.temporaryDirectory.appendingPathComponent("Outgoing", isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let existing = Set((try? FileManager.default.contentsOfDirectory(atPath: folder.path)) ?? [])
            let name = TransferPlan.safeFileName(received.file.lastPathComponent, existing: existing)
            let target = folder.appendingPathComponent(name)
            try FileManager.default.copyItem(at: received.file, to: target)
            return PickedFile(url: target)
        }
    }
}

extension OutgoingFile {
    /// Describes a local file for an offer.
    /// - Parameter url: File to send.
    init(fileAt url: URL) throws {
        let size = (try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.uint64Value ?? 0
        self.init(name: url.lastPathComponent, size: size, mimeType: TransferPlan.mimeType(forExtension: url.pathExtension), url: url)
    }
}
