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

    /// A clipboard copied while Fenpo was away, offered with the system Paste button.
    struct ClipboardOffer: Equatable {
        /// What the pasteboard metadata says it holds.
        enum Kind: Equatable {
            case text
            case link
            case image
        }

        let changeCount: Int
        let kind: Kind
        /// Marked private, or the system sees a number that may be a code.
        let isSensitive: Bool
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
    private let watchesClipboard: Bool
    /// Fenpo came to the front while the PC was not reachable yet: check the clipboard once it is.
    @ObservationIgnored private var isClipboardCheckWaiting = false

    /// Touchpad, keyboard and presentation remote; `nil` in previews.
    let pcInput: PcInputService?

    /// The Personal Hotspot details for the PC; `nil` in previews.
    let hotspot: HotspotService?

    /// Sends new screenshots to the PC; `nil` in previews.
    let screenshots: ScreenshotSender?

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

    /// Transfers opened on the Transfers tab.
    var transfersPath: [TransferRoute] = []

    @ObservationIgnored private var isRefreshScheduled = false

    /// The pairing flow is on screen.
    var isPairingPresented = false

    /// Result of the last action, if it is still on screen.
    var toast: Toast?

    /// The clipboard offered on Home, if any.
    var clipboardOffer: ClipboardOffer?

    /// The PC is ringing this iPhone and Fenpo is in front.
    var isRinging = false

    /// The camera screen's state while the iPhone is the PC's webcam.
    var cameraState: CameraService.UIState?

    /// The camera screen opened from Home before the PC asked: it says how to start the camera
    /// on the PC and goes live when the PC's CameraStart arrives.
    var isCameraReadyPresented = false

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
    ///   - watchesClipboard: Offers or sends a new clipboard when Fenpo comes to the front; off in
    ///     previews and screenshot scenes.
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
        watchesClipboard: Bool = false
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
        self.watchesClipboard = watchesClipboard
        if watchesClipboard {
            PhoneClipboard.onHandled = { [weak self] in
                self?.clipboardOffer = nil
            }
        }
    }

    /// The camera's live picture.
    var cameraSession: AVCaptureSession? {
        camera?.previewSession
    }

    /// Camera screen actions.
    var cameraActions: CameraActions {
        guard let camera else {
            return CameraActions(stop: { [weak self] in
                self?.cameraState = nil
                self?.isCameraReadyPresented = false
            })
        }
        return CameraActions(
            stop: { [weak self] in
                self?.isCameraReadyPresented = false
                camera.stopFromPhone()
            },
            zoom: { camera.setZoom($0) },
            centerStage: { camera.setCenterStage($0) },
            flip: { camera.flip() }
        )
    }

    /// Fenpo came to the front: refresh, stop a background ring, ask for notifications once paired.
    func becameActive() async {
        await refresh()
        await checkClipboard()
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
        if isClipboardCheckWaiting, snapshot.isConnected, UIApplication.shared.applicationState == .active {
            isClipboardCheckWaiting = false
            Task {
                await checkClipboard()
            }
        }
        if readsPermissions {
            permissions = PermissionsSnapshot(
                bluetooth: Self.bluetoothPermission(linkUp: snapshot.links.bluetooth, paired: snapshot.pc != nil),
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

    /// Pairing goes through AccessorySetupKit, which gives Fenpo its accessory without the system's
    /// Bluetooth prompt: the authorization then stays "not determined" although the link works.
    private static func bluetoothPermission(linkUp: Bool, paired: Bool) -> PermissionState {
        if linkUp {
            return .allowed
        }
        switch CBManager.authorization {
        case .allowedAlways:
            return .allowed
        case .denied, .restricted:
            return .denied
        case .notDetermined:
            return paired ? .allowed : .notDetermined
        @unknown default:
            return .unknown
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

    /// Controls what plays on the PC; only failures show a message.
    func pcMedia(_ command: PCMediaCommand) {
        Task {
            do {
                try await actions.pcMedia(command)
            } catch let error as FenpoActionError {
                show(Toast(message: error.localizedStringResource, isError: true))
            } catch {
                show(Toast(message: "action.error.generic", isError: true))
            }
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
            await sendReadClipboard(success: "home.toast.sent")
        }
    }

    /// Sends what the system Paste button handed over; no paste prompt.
    /// - Parameter providers: Items from `PasteButton`.
    func sendPasted(_ providers: [NSItemProvider]) {
        let isMarked = PhoneClipboard.isMarkedSensitive
        Task {
            await run(success: "home.toast.sent") {
                guard let payload = await PhoneClipboard.payload(from: providers, isSensitive: isMarked) else {
                    throw FenpoActionError.emptyText
                }
                try await self.actions.sendClipboard(payload)
                PhoneClipboard.markHandled()
            }
        }
    }

    /// Hides the offer and does not offer this clipboard again.
    func dismissClipboardOffer() {
        PhoneClipboard.markHandled()
        clipboardOffer = nil
    }

    /// Puts the PC's latest clipboard text on the phone.
    func pasteFromPC() {
        Task {
            await run(success: "home.toast.pasted") {
                UIPasteboard.general.string = try await self.actions.fetchPCClipboard()
                PhoneClipboard.markHandled()
            }
        }
    }

    /// "Açılınca panoyu gönder": offers or sends a clipboard copied while Fenpo was away. Decides on
    /// pasteboard metadata, which needs no paste prompt; each clipboard is read at most once.
    private func checkClipboard() async {
        guard watchesClipboard else {
            return
        }
        let defaults = UserDefaults(suiteName: FenpoIdentifiers.appGroup)
        let changeCount = PhoneClipboard.changeCount
        guard let handled = ClipboardOnOpen.handledChangeCount(in: defaults) else {
            // First launch: what was copied before Fenpo was installed is not news.
            ClipboardOnOpen.markHandled(changeCount, in: defaults)
            return
        }
        guard handled != changeCount else {
            return
        }
        let hasContent = PhoneClipboard.hasContent
        let isMarked = PhoneClipboard.isMarkedSensitive
        let mightHoldCode = hasContent ? await PhoneClipboard.mightHoldCode() : false
        let isConnected = snapshot.isConnected && FeatureAccess.isAvailable(.clipboard)
        let mode = ClipboardOnOpen.mode(in: defaults)
        let decision = ClipboardOnOpen.decide(
            changeCount: changeCount,
            handled: handled,
            hasContent: hasContent,
            mightBeSensitive: isMarked || mightHoldCode,
            isConnected: isConnected,
            sharingEnabled: ClipboardOnOpen.isSharingEnabled(in: defaults),
            mode: mode
        )
        log.info("Clipboard on open: changeCount \(handled) -> \(changeCount), content \(hasContent), marked \(isMarked), number \(mightHoldCode), connected \(isConnected), mode \(mode.rawValue): \(decision)")
        isClipboardCheckWaiting = hasContent && !isConnected && snapshot.pc != nil && mode != .off
        switch decision {
        case .nothing:
            break
        case .offer:
            let pasteboard = UIPasteboard.general
            let kind: ClipboardOffer.Kind = pasteboard.hasImages ? .image : (pasteboard.hasURLs ? .link : .text)
            clipboardOffer = ClipboardOffer(changeCount: changeCount, kind: kind, isSensitive: isMarked || mightHoldCode)
        case .send:
            await sendReadClipboard(success: "home.toast.sentOnOpen")
        }
    }

    /// Reads the clipboard (iOS may ask) and sends it. A clipboard that could not be read is not
    /// tried again, so the prompt shows once per copy.
    private func sendReadClipboard(success: LocalizedStringResource) async {
        let hadContent = PhoneClipboard.hasContent
        guard let payload = PhoneClipboard.read() else {
            PhoneClipboard.markHandled()
            show(Toast(message: hadContent ? "home.toast.pasteNotAllowed" : "action.error.emptyText", isError: true))
            return
        }
        await run(success: success) {
            try await self.actions.sendClipboard(payload)
            PhoneClipboard.markHandled()
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
