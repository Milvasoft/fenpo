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

struct PCMediaTests {
    func media(playing: Bool, duration: UInt64 = 200_000, volume: Int = 50) -> PCMedia {
        PCMedia(appName: "Spotify", title: "", artist: "", isPlaying: playing, durationMs: duration, positionMs: 10_000, positionAt: Date(timeIntervalSince1970: 1_000), volumePercent: volume, canPrevious: true, canNext: true)
    }

    @Test
    func positionMovesOnlyWhilePlaying() {
        let now = Date(timeIntervalSince1970: 1_005)
        #expect(media(playing: true).position(at: now) == 15_000)
        #expect(media(playing: false).position(at: now) == 10_000)
        #expect(media(playing: true, duration: 12_000).position(at: now) == 12_000)
    }

    @Test
    func volumeStepsStayInRange() {
        #expect(media(playing: true, volume: 95).volumeStep(up: true) == 100)
        #expect(media(playing: true, volume: 5).volumeStep(up: false) == 0)
        #expect(media(playing: true, volume: 50).volumeStep(up: true) == 60)
    }

    @Test
    func titleFallsBackToTheApp() {
        #expect(media(playing: true).displayTitle == "Spotify")
    }
}

struct DemoContentTests {
    /// The App Store screenshots show the sample session: made-up names only.
    @Test
    func demoContentIsMadeUpInBothLanguages() {
        let turkish = SampleSession.pairedSnapshot(turkish: true)
        let english = SampleSession.pairedSnapshot(turkish: false)
        #expect(turkish.pc?.name == "Ofis PC")
        #expect(english.pc?.name == "Office PC")
        #expect(english.pcMedia?.appName == "Radio")
        #expect(english.transfers.first?.fileName == "Contract_v3.pdf")
    }
}
