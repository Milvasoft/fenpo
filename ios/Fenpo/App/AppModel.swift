import FenpoApple
import FenpoCore
import AVFoundation
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
    private let camera: CameraService?
    private let transferActivities: TransferActivities?

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

    /// The camera screen's state while the iPhone is the PC's webcam.
    var cameraState: CameraService.UIState?

    /// Creates the model.
    /// - Parameters:
    ///   - session: Source of the session state.
    ///   - pairing: Runs the pairing steps.
    ///   - actions: Clipboard and PC actions.
    ///   - ringer: Plays the ring the PC asks for; `nil` in previews.
    ///   - camera: Fenpo Kamera; `nil` in previews.
    ///   - transferActivities: Live Activities of large transfers; `nil` in previews.
    init(
        session: any SessionProviding,
        pairing: any PairingProviding = SamplePairing(),
        actions: any FenpoActions = SampleActions(),
        ringer: PhoneRinger? = nil,
        camera: CameraService? = nil,
        transferActivities: TransferActivities? = nil
    ) {
        self.session = session
        self.pairing = pairing
        self.actions = actions
        self.ringer = ringer
        self.camera = camera
        self.transferActivities = transferActivities
    }

    /// The camera's live picture.
    var cameraSession: AVCaptureSession? {
        camera?.previewSession
    }

    /// Camera screen actions.
    var cameraActions: CameraActions {
        guard let camera else {
            return CameraActions(stop: { [weak self] in self?.cameraState = nil })
        }
        return CameraActions(
            stop: { camera.stopFromPhone() },
            zoom: { camera.setZoom($0) },
            centerStage: { camera.setCenterStage($0) },
            flip: { camera.flip() }
        )
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
        if let camera {
            cameraState = camera.state
        }
        transferActivities?.update(snapshot.activeTransfers, pcName: snapshot.pc?.name ?? "")
    }

    /// The Focus switch in Settings changed: tells the PC the state that now applies.
    func focusSyncChanged() async {
        guard let active = UserDefaults(suiteName: FenpoIdentifiers.appGroup)?.object(forKey: FocusSync.stateKey) as? Bool else {
            return
        }
        do {
            try await actions.reportFocus(active)
        } catch {
            log.info("Focus state not delivered now: \(String(describing: error))")
        }
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

    /// The photo picker is on screen.
    var isPhotoPickerPresented = false

    /// Opens the photo picker.
    func sendPhotos() {
        isPhotoPickerPresented = true
    }

    /// Sends picked or shared files to the PC and removes the temporary copies afterwards.
    /// - Parameter urls: Local files.
    func sendFiles(_ urls: [URL]) {
        guard !urls.isEmpty else {
            return
        }
        Task {
            await run(success: "home.toast.filesSent") {
                defer {
                    for url in urls {
                        try? FileManager.default.removeItem(at: url)
                    }
                }
                let files = try urls.map(OutgoingFile.init(fileAt:))
                try await self.actions.sendFiles(files)
            }
            await refresh()
        }
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
