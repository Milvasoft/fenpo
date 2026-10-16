import FenpoCore
import FenpoProtocol
import Foundation
import Testing

struct TransferPlanTests {
    @Test
    func offerListsFilesInOrderWithTotal() {
        let files = [
            OutgoingFile(fileID: "a", name: "IMG_1.HEIC", size: 10, mimeType: "image/heic", url: URL(fileURLWithPath: "/tmp/a")),
            OutgoingFile(fileID: "b", name: "notes.txt", size: 5, mimeType: "text/plain", url: URL(fileURLWithPath: "/tmp/b")),
        ]
        let offer = TransferPlan.offer(transferID: "t1", files: files)
        #expect(offer.transferID == "t1")
        #expect(offer.files.map(\.name) == ["IMG_1.HEIC", "notes.txt"])
        #expect(offer.totalBytes == 15)
    }

    @Test
    func headerFrameIsABareFramedHeader() throws {
        let frame = try TransferPlan.headerFrame(transferID: "t1")
        var decoder = EnvelopeFrameDecoder()
        let payload = try #require(try decoder.append(frame).first)
        let header = try Fenpo_V1_TransferStreamHeader(serializedBytes: payload)
        #expect(header.transferID == "t1")
    }

    @Test(arguments: [
        ("report.pdf", Set<String>(), "report.pdf"),
        ("report.pdf", ["report.pdf"], "report (2).pdf"),
        ("report.pdf", ["report.pdf", "report (2).pdf"], "report (3).pdf"),
        ("../../etc/passwd", [], "passwd"),
        ("C:\\Users\\x\\a.txt", [], "a.txt"),
        (".hidden", [], "hidden"),
        ("", [], "file"),
        ("README", ["README"], "README (2)"),
    ] as [(String, Set<String>, String)])
    func safeFileNames(proposed: String, existing: Set<String>, expected: String) {
        #expect(TransferPlan.safeFileName(proposed, existing: existing) == expected)
    }

    @Test
    func mimeTypes() {
        #expect(TransferPlan.mimeType(forExtension: "HEIC") == "image/heic")
        #expect(TransferPlan.mimeType(forExtension: "weird") == "application/octet-stream")
    }
}

struct IncomingFileSplitterTests {
    @Test
    func splitsAcrossFilesWithAnyChunking() {
        let sizes: [UInt64] = [3, 0, 4, 2]
        let stream: [UInt8] = Array(1...9)
        for chunk in 1...9 {
            var splitter = IncomingFileSplitter(sizes: sizes)
            var files: [Int: [UInt8]] = [:]
            var completed: [Int] = []
            var offset = 0
            while offset < stream.count {
                let end = min(offset + chunk, stream.count)
                for piece in splitter.consume(Array(stream[offset..<end])) {
                    files[piece.fileIndex, default: []] += piece.bytes
                    if piece.completesFile {
                        completed.append(piece.fileIndex)
                    }
                }
                offset = end
            }
            #expect(files[0] == [1, 2, 3], "chunk \(chunk)")
            #expect(files[2] == [4, 5, 6, 7])
            #expect(files[3] == [8, 9])
            #expect(completed == [0, 2, 3])
            #expect(splitter.isComplete)
            #expect(splitter.bytesDone == 9)
        }
    }

    @Test
    func ignoresExtraBytesAndHandlesOnlyEmptyFiles() {
        var splitter = IncomingFileSplitter(sizes: [2])
        let pieces = splitter.consume([1, 2, 3, 4])
        #expect(pieces.map(\.bytes) == [[1, 2]])
        #expect(splitter.isComplete)

        let empty = IncomingFileSplitter(sizes: [0, 0])
        #expect(empty.isComplete)
    }
}
