import FenpoCore
import Observation
import OSLog

/// App wide state: the session with the paired PC and what the root view presents.
@MainActor
@Observable
final class AppModel {
    /// Tabs of the root tab bar.
    enum Tab: Hashable {
        case home
        case transfers
        case pc
        case settings
    }

    private let session: any SessionProviding

    /// Runs the pairing steps.
    let pairing: any PairingProviding
    private let logger = Logger(subsystem: "com.fenpo.app", category: "AppModel")

    /// Latest state of the session.
    var snapshot: SessionSnapshot = .unpaired

    /// Permissions shown in Settings.
    var permissions: PermissionsSnapshot = .sample

    /// Selected tab.
    var selectedTab: Tab = .home

    /// The pairing flow is on screen.
    var isPairingPresented = false

    /// Creates the model.
    /// - Parameters:
    ///   - session: Source of the session state.
    ///   - pairing: Runs the pairing steps.
    init(session: any SessionProviding, pairing: any PairingProviding = SamplePairing()) {
        self.session = session
        self.pairing = pairing
    }

    /// Reloads the session state.
    func refresh() async {
        snapshot = await session.snapshot()
    }

    /// Asks the PC to lock.
    func lockPC() async {
        do {
            try await session.lockPC()
        } catch {
            logger.error("Lock PC failed: \(error.localizedDescription, privacy: .public)")
        }
    }

    /// Forgets the paired PC.
    func unpair() async {
        do {
            try await session.unpair()
        } catch {
            logger.error("Unpair failed: \(error.localizedDescription, privacy: .public)")
        }
        await refresh()
    }

    /// Sends the phone's clipboard to the PC. Arrives with the Fenpo channels.
    func sendClipboard() {
        logger.info("Send clipboard tapped; channels not available yet")
    }

    /// Puts the PC's latest clipboard item on the phone. Arrives with the Fenpo channels.
    func pasteFromPC() {
        logger.info("Paste from PC tapped; channels not available yet")
    }

    /// Sends the latest photos to the PC. Arrives with file transfer.
    func sendPhotos() {
        logger.info("Send photos tapped; file transfer not available yet")
    }

    /// Called when the pairing flow finishes.
    func pairingFinished() async {
        isPairingPresented = false
        await refresh()
    }
}
