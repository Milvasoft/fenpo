import Foundation

/// Everything the home screen, the PC tab and the widgets show about the paired PC.
public struct SessionSnapshot: Equatable, Sendable {
    /// The paired PC, or `nil` before pairing.
    public var pc: PairedPC?

    /// Connection paths that are up.
    public var links: ChannelLinks

    /// Distance to the PC from the BLE signal.
    public var proximity: Proximity

    /// Start of the text that "Send clipboard" would send.
    public var phoneClipboardPreview: String?

    /// Start of the PC's latest clipboard item.
    public var pcClipboardPreview: String?

    /// Recent photos that are ready to send.
    public var readyPhotoCount: Int

    /// Latest transfers, newest first.
    public var transfers: [TransferRecord]

    /// Transfers in progress.
    public var activeTransfers: [ActiveTransfer]

    /// Creates a snapshot.
    public init(
        pc: PairedPC?,
        links: ChannelLinks,
        proximity: Proximity,
        phoneClipboardPreview: String?,
        pcClipboardPreview: String?,
        readyPhotoCount: Int,
        transfers: [TransferRecord],
        activeTransfers: [ActiveTransfer] = []
    ) {
        self.pc = pc
        self.links = links
        self.proximity = proximity
        self.phoneClipboardPreview = phoneClipboardPreview
        self.pcClipboardPreview = pcClipboardPreview
        self.readyPhotoCount = readyPhotoCount
        self.transfers = transfers
        self.activeTransfers = activeTransfers
    }

    /// State before any pairing.
    public static let unpaired = SessionSnapshot(
        pc: nil,
        links: .none,
        proximity: .unknown,
        phoneClipboardPreview: nil,
        pcClipboardPreview: nil,
        readyPhotoCount: 0,
        transfers: []
    )

    /// The PC is paired and at least one path is up.
    public var isConnected: Bool {
        pc != nil && links.isConnected
    }
}

/// A transfer that is still moving.
public struct ActiveTransfer: Equatable, Sendable, Identifiable {
    /// Transfer id from the offer.
    public let id: String

    /// First file name.
    public let firstFileName: String

    /// Number of files.
    public let fileCount: Int

    /// Which way the files go.
    public let direction: TransferRecord.Direction

    /// Bytes moved so far.
    public var bytesDone: UInt64

    /// Bytes in the offer.
    public let totalBytes: UInt64

    /// Files moved completely.
    public var filesDone: Int

    /// Creates a summary.
    public init(id: String, firstFileName: String, fileCount: Int, direction: TransferRecord.Direction, bytesDone: UInt64 = 0, totalBytes: UInt64, filesDone: Int = 0) {
        self.id = id
        self.firstFileName = firstFileName
        self.fileCount = fileCount
        self.direction = direction
        self.bytesDone = bytesDone
        self.totalBytes = totalBytes
        self.filesDone = filesDone
    }

    /// Files not yet moved completely.
    public var filesLeft: Int {
        max(0, fileCount - filesDone)
    }

    /// Progress between 0 and 1.
    public var fraction: Double {
        totalBytes == 0 ? 1 : min(1, Double(bytesDone) / Double(totalBytes))
    }
}

/// Source of the session state and the actions the UI can trigger.
///
/// The real implementation sits on the Fenpo channels; `SampleSession` stands in until then.
public protocol SessionProviding: Sendable {
    /// Returns the current state.
    func snapshot() async -> SessionSnapshot

    /// Asks the PC to lock its Windows session.
    func lockPC() async throws

    /// Forgets the paired PC.
    func unpair() async throws
}
