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

    /// Creates a snapshot.
    public init(
        pc: PairedPC?,
        links: ChannelLinks,
        proximity: Proximity,
        phoneClipboardPreview: String?,
        pcClipboardPreview: String?,
        readyPhotoCount: Int,
        transfers: [TransferRecord]
    ) {
        self.pc = pc
        self.links = links
        self.proximity = proximity
        self.phoneClipboardPreview = phoneClipboardPreview
        self.pcClipboardPreview = pcClipboardPreview
        self.readyPhotoCount = readyPhotoCount
        self.transfers = transfers
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
