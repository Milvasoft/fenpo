import FenpoApple
import FenpoCore
import AVFoundation
import CoreBluetooth
import Photos
import Foundation
import Observation
import UIKit

/// App wide state: the session with the paired PC and what the root view presents.
@MainActor
@Observable
final class AppModel {
    /// Screens pushed on the PC tab.
    enum PCScreen: Hashable {
        case touchpad
        case presenter
    }

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
    private let readsPermissions: Bool

    /// Touchpad, keyboard and presentation remote; `nil` in previews.
    let pcInput: PcInputService?

    /// The Personal Hotspot details for the PC; `nil` in previews.
    let hotspot: HotspotService?

    /// Sends new screenshots to the PC; `nil` in previews.
    let screenshots: ScreenshotSender?

    /// Unlocking the PC with Face ID; `nil` in previews.
    let unlock: UnlockService?

    /// The PC waits for Face ID.
    var unlockPrompt: UnlockService.Prompt?

    /// A key for unlocking the paired PC exists.
    var isUnlockSetUp = false

    /// Runs the pairing steps.
    let pairing: any PairingProviding
    private let log = FenpoLog("AppModel")

    /// Latest state of the session.
    var snapshot: SessionSnapshot = .unpaired

    /// Permissions shown in Settings.
    var permissions: PermissionsSnapshot = .sample

    /// Selected tab.
    var selectedTab: Tab = .home

    /// Screens pushed on the PC tab.
    var pcPath: [PCScreen] = []

    @ObservationIgnored private var isRefreshScheduled = false

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
    ///   - readsPermissions: Reads the real permissions; previews keep the sample.
    ///   - pcInput: Touchpad and presentation remote; `nil` in previews.
    ///   - hotspot: Personal Hotspot details; `nil` in previews.
    ///   - screenshots: Screenshot sending; `nil` in previews.
    ///   - unlock: Unlocking the PC with Face ID; `nil` in previews.
    init(
        session: any SessionProviding,
        pairing: any PairingProviding = SamplePairing(),
        actions: any FenpoActions = SampleActions(),
        ringer: PhoneRinger? = nil,
        camera: CameraService? = nil,
        transferActivities: TransferActivities? = nil,
        readsPermissions: Bool = false,
        pcInput: PcInputService? = nil,
        hotspot: HotspotService? = nil,
        screenshots: ScreenshotSender? = nil,
        unlock: UnlockService? = nil
    ) {
        self.session = session
        self.pairing = pairing
        self.actions = actions
        self.ringer = ringer
        self.camera = camera
        self.transferActivities = transferActivities
        self.readsPermissions = readsPermissions
        self.pcInput = pcInput
        self.hotspot = hotspot
        self.screenshots = screenshots
        self.unlock = unlock
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

    /// Reloads the session state soon, at most ten times a second however often the services
    /// report a change (a transfer reports every chunk).
    func scheduleRefresh() {
        guard !isRefreshScheduled else {
            return
        }
        isRefreshScheduled = true
        Task {
            try? await Task.sleep(for: .milliseconds(100))
            isRefreshScheduled = false
            await refresh()
        }
    }

    /// Reloads the session state.
    func refresh() async {
        snapshot = await session.snapshot()
        if let camera {
            cameraState = camera.state
        }
        transferActivities?.update(snapshot.activeTransfers, pcName: snapshot.pc?.name ?? "")
        if let unlock {
            unlockPrompt = unlock.prompt
            isUnlockSetUp = unlock.isSetUp
        }
        if readsPermissions {
            permissions = PermissionsSnapshot(
                bluetooth: Self.bluetoothPermission,
                // iOS has no API for these: local network is known only once the Wi-Fi channel
                // worked, and the paste setting never.
                localNetwork: snapshot.links.wifi ? .allowed : .unknown,
                paste: .unknown,
                photos: Self.photosPermission
            )
        }
    }

    private static var photosPermission: PermissionState {
        switch PHPhotoLibrary.authorizationStatus(for: .readWrite) {
        case .authorized, .limited:
            .allowed
        case .denied, .restricted:
            .denied
        case .notDetermined:
            .notDetermined
        @unknown default:
            .unknown
        }
    }

    /// Asks for photo access the first time; later the switch is in iOS Settings.
    func requestPhotoAccess() async {
        _ = await PHPhotoLibrary.requestAuthorization(for: .readWrite)
        await refresh()
    }

    private static var bluetoothPermission: PermissionState {
        switch CBManager.authorization {
        case .allowedAlways:
            .allowed
        case .denied, .restricted:
            .denied
        case .notDetermined:
            .notDetermined
        @unknown default:
            .unknown
        }
    }

    /// Why the touchpad and the presentation remote do nothing now, with what to do; `nil` when
    /// they work (and in previews).
    var remoteProblem: LocalizedStringResource? {
        guard let pcInput else {
            return nil
        }
        if !snapshot.isConnected {
            return "action.error.notConnected"
        }
        return pcInput.isAvailable ? nil : "action.error.unsupportedByPC"
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

    /// Face ID, then the key goes to the PC.
    func approveUnlock() async {
        await unlock?.approve()
        await refresh()
    }

    /// The user does not want the PC unlocked now.
    func declineUnlock() {
        unlock?.decline()
        unlockPrompt = nil
    }

    /// Forgets the key; the PC asks for its password again.
    func removeUnlock() {
        unlock?.removeKey()
        isUnlockSetUp = false
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
