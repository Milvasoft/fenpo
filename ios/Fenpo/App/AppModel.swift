import FenpoApple
import FenpoCore
import Foundation
import Observation
import UIKit

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

    /// A short message shown over the tab bar after an action.
    struct Toast: Identifiable {
        let id = UUID()
        let message: LocalizedStringResource
        let isError: Bool
    }

    private let session: any SessionProviding
    private let actions: any FenpoActions
    private let ringer: PhoneRinger?

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

    /// Result of the last action, if it is still on screen.
    var toast: Toast?

    /// The PC is ringing this iPhone and Fenpo is in front.
    var isRinging = false

    /// Creates the model.
    /// - Parameters:
    ///   - session: Source of the session state.
    ///   - pairing: Runs the pairing steps.
    ///   - actions: Clipboard and PC actions.
    ///   - ringer: Plays the ring the PC asks for; `nil` in previews.
    init(
        session: any SessionProviding,
        pairing: any PairingProviding = SamplePairing(),
        actions: any FenpoActions = SampleActions(),
        ringer: PhoneRinger? = nil
    ) {
        self.session = session
        self.pairing = pairing
        self.actions = actions
        self.ringer = ringer
    }

    /// Fenpo came to the front: refresh, stop a background ring, ask for notifications once paired.
    func becameActive() async {
        await refresh()
        if !isRinging {
            ringer?.stop()
        }
        if snapshot.pc != nil {
            await ringer?.requestPermissionIfNeeded()
        }
    }

    /// Stops the ring.
    func stopRinging() {
        ringer?.stop()
        isRinging = false
    }

    /// Reloads the session state.
    func refresh() async {
        snapshot = await session.snapshot()
    }

    /// Asks the PC to lock.
    func lockPC() async {
        await run(success: "home.toast.locked") {
            try await self.actions.lockPC()
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

    /// Sends the phone's clipboard to the PC. Reading it may show the iOS paste prompt.
    func sendClipboard() {
        Task {
            await run(success: "home.toast.sent") {
                guard let text = UIPasteboard.general.string, !text.isEmpty else {
                    throw FenpoActionError.emptyText
                }
                try await self.actions.sendText(text)
            }
        }
    }

    /// Puts the PC's latest clipboard text on the phone.
    func pasteFromPC() {
        Task {
            await run(success: "home.toast.pasted") {
                UIPasteboard.general.string = try await self.actions.fetchPCClipboard()
            }
        }
    }

    /// Sends the latest photos to the PC. Arrives with file transfer (Windows data channel pending).
    func sendPhotos() {
        log.info("Send photos tapped; file transfer not available yet")
    }

    /// Called when the pairing flow finishes.
    func pairingFinished() async {
        isPairingPresented = false
        await refresh()
    }

    private func run(success: LocalizedStringResource, _ action: @MainActor () async throws -> Void) async {
        do {
            try await action()
            show(Toast(message: success, isError: false))
        } catch let error as FenpoActionError {
            log.info("Action failed: \(error)")
            show(Toast(message: error.localizedStringResource, isError: true))
        } catch {
            log.error("Action failed: \(String(describing: error))")
            show(Toast(message: "action.error.generic", isError: true))
        }
    }

    private func show(_ toast: Toast) {
        self.toast = toast
        Task {
            try? await Task.sleep(for: .seconds(3))
            if self.toast?.id == toast.id {
                self.toast = nil
            }
        }
    }
}
