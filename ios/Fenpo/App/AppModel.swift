import FenpoApple
import FenpoCore
import Observation

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
    private let log = FenpoLog("AppModel")

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
            log.error("Lock PC failed: \(error.localizedDescription)")
        }
    }

    /// Forgets the paired PC.
    func unpair() async {
        do {
            try await session.unpair()
        } catch {
            log.error("Unpair failed: \(error.localizedDescription)")
        }
        await refresh()
    }

    /// Sends the phone's clipboard to the PC. Arrives with the Fenpo channels.
    func sendClipboard() {
        log.info("Send clipboard tapped; channels not available yet")
    }

    /// Puts the PC's latest clipboard item on the phone. Arrives with the Fenpo channels.
    func pasteFromPC() {
        log.info("Paste from PC tapped; channels not available yet")
    }

    /// Sends the latest photos to the PC. Arrives with file transfer.
    func sendPhotos() {
        log.info("Send photos tapped; file transfer not available yet")
    }

    /// Called when the pairing flow finishes.
    func pairingFinished() async {
        isPairingPresented = false
        await refresh()
    }
}
