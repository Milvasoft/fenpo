import Foundation

/// Sample data shaped like design/mobile/mobile.html, used until the channels exist and in previews.
public actor SampleSession: SessionProviding {
    private var current: SessionSnapshot

    /// Creates a sample session.
    /// - Parameters:
    ///   - paired: Start with a paired, connected PC.
    ///   - now: Reference time for the transfer dates.
    public init(paired: Bool = true, now: Date = .now) {
        current = paired ? Self.pairedSnapshot(now: now) : .unpaired
    }

    /// Creates a sample session with a given state.
    /// - Parameter snapshot: The state to show.
    public init(snapshot: SessionSnapshot) {
        current = snapshot
    }

    public func snapshot() async -> SessionSnapshot {
        current
    }

    public func lockPC() async throws {
    }

    public func unpair() async throws {
        current = .unpaired
    }

    /// The paired state from the design, with demo content in the app's language: no real names,
    /// people, songs or other apps' trademarks, since the App Store screenshots show it.
    /// - Parameters:
    ///   - now: Reference time for the transfer dates.
    ///   - turkish: Turkish demo content; by default from the preferred language.
    public static func pairedSnapshot(now: Date = .now, turkish: Bool = Locale.preferredLanguages.first?.hasPrefix("tr") ?? false) -> SessionSnapshot {
        func id(_ number: Int) -> UUID {
            UUID(uuidString: "00000000-0000-0000-0000-00000000000\(number)")!
        }
        let demo = turkish ? Demo.turkish : Demo.english
        return SessionSnapshot(
            pc: PairedPC(id: "sample-pc", name: demo.pcName, systemName: "Windows 11"),
            links: ChannelLinks(bluetooth: true, wifi: true, usb: false),
            proximity: .near,
            phoneClipboardPreview: demo.phoneClipboard,
            pcClipboardPreview: demo.pcClipboard,
            readyPhotoCount: 3,
            transfers: [
                TransferRecord(id: id(1), fileName: demo.files[0], direction: .fromPC, date: now.addingTimeInterval(-120), localFileName: demo.files[0]),
                TransferRecord(id: id(2), fileName: "IMG_4835.HEIC", direction: .toPC, date: now.addingTimeInterval(-3_600), folder: demo.picturesFolder),
                TransferRecord(id: id(3), fileName: demo.files[1], direction: .fromPC, date: now.addingTimeInterval(-86_400), localFileName: demo.files[1]),
                TransferRecord(id: id(4), fileName: demo.files[2], direction: .toPC, date: now.addingTimeInterval(-172_800), folder: demo.documentsFolder),
            ],
            activeTransfers: [
                ActiveTransfer(id: "sample", firstFileName: "IMG_4836.MOV", fileCount: 1, direction: .toPC, bytesDone: 123_731_968, totalBytes: 192_937_984),
            ],
            pcMedia: PCMedia(
                appName: demo.player,
                title: demo.song,
                artist: demo.artist,
                isPlaying: true,
                durationMs: 248_000,
                positionMs: 81_000,
                positionAt: now,
                volumePercent: 60,
                canPrevious: true,
                canNext: true
            )
        )
    }

    /// Made-up content for the sample session.
    private struct Demo {
        let pcName: String
        let phoneClipboard: String
        let pcClipboard: String
        let files: [String]
        let picturesFolder: String
        let documentsFolder: String
        let player: String
        let song: String
        let artist: String

        static let turkish = Demo(
            pcName: "Ofis PC",
            phoneClipboard: "Toplantı 15.30’da, B salonu",
            pcClipboard: "Üçüncü çeyrek raporunun son hali ekte",
            files: ["Sözleşme_v3.pdf", "Bütçe_Ekim.xlsx", "Teklif v2.docx"],
            picturesFolder: "Resimler",
            documentsFolder: "Belgeler",
            player: "Radyo",
            song: "Sabah Yolu",
            artist: "Fenerler"
        )

        static let english = Demo(
            pcName: "Office PC",
            phoneClipboard: "Meeting at 3:30 pm, room B",
            pcClipboard: "Q3 report, final version attached",
            files: ["Contract_v3.pdf", "Budget_October.xlsx", "Proposal v2.docx"],
            picturesFolder: "Pictures",
            documentsFolder: "Documents",
            player: "Radio",
            song: "Morning Drive",
            artist: "The Lanterns"
        )
    }
}
