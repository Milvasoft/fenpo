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

    public func snapshot() async -> SessionSnapshot {
        current
    }

    public func lockPC() async throws {
    }

    public func unpair() async throws {
        current = .unpaired
    }

    /// The paired state from the design.
    /// - Parameter now: Reference time for the transfer dates.
    public static func pairedSnapshot(now: Date = .now) -> SessionSnapshot {
        SessionSnapshot(
            pc: PairedPC(id: "sample-pc", name: "BUGRA-PC", systemName: "Windows 11"),
            links: ChannelLinks(bluetooth: true, wifi: true, usb: false),
            proximity: .near,
            phoneClipboardPreview: "Doğrulama kodun 482 913",
            pcClipboardPreview: "docker compose -f deploy/compose.yml up -d",
            readyPhotoCount: 3,
            transfers: [
                TransferRecord(id: UUID(uuidString: "00000000-0000-0000-0000-000000000001")!, fileName: "Sözleşme_v3.pdf", direction: .fromPC, date: now.addingTimeInterval(-120), localFileName: "Sözleşme_v3.pdf"),
                TransferRecord(id: UUID(uuidString: "00000000-0000-0000-0000-000000000002")!, fileName: "IMG_4835.HEIC", direction: .toPC, date: now.addingTimeInterval(-3_600), folder: "Resimler"),
                TransferRecord(id: UUID(uuidString: "00000000-0000-0000-0000-000000000003")!, fileName: "Bütçe_Ekim.xlsx", direction: .fromPC, date: now.addingTimeInterval(-86_400), localFileName: "Bütçe_Ekim.xlsx"),
                TransferRecord(id: UUID(uuidString: "00000000-0000-0000-0000-000000000004")!, fileName: "Teklif v2.docx", direction: .toPC, date: now.addingTimeInterval(-172_800), folder: "Belgeler"),
            ],
            activeTransfers: [
                ActiveTransfer(id: "sample", firstFileName: "IMG_4836.MOV", fileCount: 1, direction: .toPC, bytesDone: 123_731_968, totalBytes: 192_937_984),
            ]
        )
    }
}
