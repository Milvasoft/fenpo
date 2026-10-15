import FenpoCore
import Foundation
import Testing

struct SessionModelTests {
    @Test(arguments: [
        ("Sözleşme_v3.pdf", "PDF", FileKind.pdf),
        ("IMG_4835.HEIC", "HEIC", .image),
        ("Teklif v2.docx", "DOCX", .document),
        ("Bütçe_Ekim.xlsx", "XLSX", .spreadsheet),
        ("clip.mov", "MOV", .video),
        ("README", "", .other),
        (".gitignore", "", .other),
    ])
    func transferFileKind(fileName: String, fileExtension: String, kind: FileKind) {
        let record = TransferRecord(fileName: fileName, direction: .toPC, date: .now)
        #expect(record.fileExtension == fileExtension)
        #expect(record.kind == kind)
    }

    @Test
    func sampleSessionUnpairs() async throws {
        let session = SampleSession()
        #expect(await session.snapshot().isConnected)

        try await session.unpair()
        let snapshot = await session.snapshot()
        #expect(snapshot.pc == nil)
        #expect(!snapshot.isConnected)
    }

    @Test
    func pairedWithoutLinksIsNotConnected() {
        var snapshot = SampleSession.pairedSnapshot()
        snapshot.links = .none
        #expect(!snapshot.isConnected)
    }
}

struct PairingProgressTests {
    @Test
    func stepsRunInOrder() {
        var progress = PairingProgress()
        #expect(progress.current == .dataChannel)
        #expect(progress.state(of: .bluetooth) == .queued)

        progress.complete(.dataChannel)
        #expect(progress.current == .bluetooth)
        #expect(progress.state(of: .bluetooth) == .inProgress)
        #expect(progress.state(of: .notifications) == .queued)

        progress.complete(.bluetooth)
        progress.complete(.notifications)
        #expect(progress.isFinished)
        #expect(progress.current == nil)
    }

    @Test
    func failedStepCanBeRetried() {
        var progress = PairingProgress()
        progress.complete(.dataChannel)
        progress.fail(.bluetooth)
        #expect(progress.current == .bluetooth)
        #expect(progress.state(of: .notifications) == .queued)

        progress.retry(.bluetooth)
        #expect(progress.state(of: .bluetooth) == .inProgress)
    }
}
