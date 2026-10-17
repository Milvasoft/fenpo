#if os(iOS)
@preconcurrency import AVFoundation
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit
import UserNotifications

/// Fenpo Kamera: the iPhone as the PC's webcam (protocol/README.md, "Camera", "Video").
///
/// iOS only lets an app use the camera in the foreground, so a `CameraStart` that arrives while
/// Fenpo is in the background is answered with `WAITING_FOR_TAP` and a notification; the camera
/// starts when Fenpo comes to the front.
@MainActor
public final class CameraService {
    /// What the camera screen shows.
    public struct UIState: Equatable, Sendable {
        public var consumer: String
        public var facing: Fenpo_V1_CameraFacing
        public var zoom: Float
        public var zoomPresets: [Float]
        public var centerStage: Bool
        public var centerStageAvailable: Bool
        public var frontAvailable: Bool
        public var isStreaming: Bool

        /// A state for previews and screenshots.
        public static let sample = UIState(
            consumer: "Teams",
            facing: .back,
            zoom: 1,
            zoomPresets: [0.5, 1, 2],
            centerStage: true,
            centerStageAvailable: true,
            frontAvailable: true,
            isStreaming: true
        )
    }

    /// The camera screen's state, or `nil` when the camera is off.
    public private(set) var state: UIState? {
        didSet {
            onChange?()
        }
    }

    /// Called when `state` changes.
    public var onChange: (() -> Void)?

    /// The capture session, for the preview.
    public var previewSession: AVCaptureSession? {
        pipeline?.session
    }

    private let wifi: WiFiChannel
    private var pendingStart: Fenpo_V1_CameraStart?
    private var request: Fenpo_V1_CameraStart?
    private var streamID: String?
    private var pipeline: CapturePipeline?
    private var encoder: H264Encoder?
    private var sender: MediaSender?
    private let slot = MediaSenderSlot()
    private var format = Fenpo_V1_VideoFormat()
    /// Changes on every start and stop, so work from an older start stops where it is.
    private var generation = 0
    private static let reconnectAttempts = 3
    private var observers: [NSObjectProtocol] = []
    private let log = FenpoLog("Camera")

    /// Creates the service.
    /// - Parameter wifi: Control channel for status messages; media connections go to the same PC.
    public init(wifi: WiFiChannel) {
        self.wifi = wifi
    }

    /// Handles camera messages from the PC; returns whether it took the message.
    /// - Parameter envelope: Message from a channel.
    public func handle(_ envelope: Fenpo_V1_Envelope) -> Bool {
        switch envelope.body {
        case .cameraStart(let start):
            Task {
                await begin(start)
            }
        case .cameraStop:
            stop(reason: "CameraStop from the PC", sendStop: false)
        case .cameraSettings(let change):
            apply(change.settings, consumer: change.consumer)
        case .requestKeyFrame(let request):
            if request.streamID.isEmpty || request.streamID == streamID {
                encoder?.requestKeyFrame()
            } else {
                log.info("RequestKeyFrame for another stream ignored")
            }
        default:
            return false
        }
        return true
    }

    /// Fenpo came to the front: start a camera the PC asked for while it was in the background.
    public func appBecameActive() {
        if let pending = pendingStart {
            pendingStart = nil
            Task {
                await begin(pending)
            }
        }
    }

    /// Stops the camera from the phone and tells the PC.
    /// - Parameter reason: For the log.
    public func stopFromPhone(reason: String = "close button") {
        stop(reason: reason, sendStop: true)
    }

    /// Zoom from the lens buttons.
    /// - Parameter ratio: Display zoom ratio.
    public func setZoom(_ ratio: Float) {
        guard var current = state else {
            return
        }
        pipeline?.setZoom(ratio)
        // Switching lenses inside the camera changes the picture: start the next frame afresh.
        encoder?.requestKeyFrame()
        current.zoom = ratio
        state = current
        sendSettingsChange()
    }

    /// Back or front camera.
    public func flip() {
        guard var current = state, let pipeline else {
            return
        }
        let facing: Fenpo_V1_CameraFacing = current.facing == .front ? .back : .front
        do {
            let capabilities = try pipeline.configure(facing: facing, fullHD: format.width >= 1920)
            current.facing = facing
            current.zoom = 1
            current.zoomPresets = capabilities.zoomPresets
            current.centerStageAvailable = capabilities.centerStageAvailable
            pipeline.setZoom(1)
            pipeline.setCenterStage(current.centerStage)
            encoder?.requestKeyFrame()
            state = current
            sendSettingsChange()
        } catch {
            log.error("Flipping the camera failed: \(String(describing: error))")
        }
    }

    /// Center Stage ("Ortala").
    /// - Parameter isOn: Desired state.
    public func setCenterStage(_ isOn: Bool) {
        guard var current = state else {
            return
        }
        pipeline?.setCenterStage(isOn)
        encoder?.requestKeyFrame()
        current.centerStage = isOn
        state = current
        sendSettingsChange()
    }

    // MARK: - Private

    private func begin(_ start: Fenpo_V1_CameraStart) async {
        // A new start replaces whatever runs; the PC gets STARTING and STREAMING for the new one.
        stop(reason: "new CameraStart", sendStop: false, reportStopped: false)
        let token = generation
        request = start
        log.info("CameraStart \(start.video.width)x\(start.video.height)@\(start.video.fps), \(start.video.bitrateKbps) kbps")
        guard UIApplication.shared.applicationState == .active else {
            pendingStart = start
            await report(.waitingForTap)
            notifyWaiting(consumer: start.consumer)
            log.info("Camera requested in the background; waiting for Fenpo to open")
            return
        }
        do {
            try await wifi.connectIfNeeded()
        } catch {
            log.error("Camera needs the Wi-Fi channel: \(String(describing: error))")
            return
        }
        guard await AVCaptureDevice.requestAccess(for: .video) else {
            await report(.failed, reason: "Camera access is off for Fenpo")
            return
        }
        guard token == generation else {
            log.info("Camera start superseded before capture")
            return
        }

        let id = UUID().uuidString
        streamID = id
        let wanted = start.video
        let fullHD = wanted.width == 0 || wanted.width >= 1920
        let pipeline = CapturePipeline()
        do {
            let facing: Fenpo_V1_CameraFacing = start.settings.facing == .front ? .front : .back
            let capabilities = try pipeline.configure(facing: facing, fullHD: fullHD)
            let size = pipeline.dimensions
            format = Fenpo_V1_VideoFormat()
            format.codec = .h264
            format.width = UInt32(size.width)
            format.height = UInt32(size.height)
            format.fps = wanted.fps == 0 ? 30 : min(wanted.fps, 30)
            format.bitrateKbps = wanted.bitrateKbps == 0 ? (fullHD ? 6000 : 3500) : min(wanted.bitrateKbps, 8000)

            var current = UIState(
                consumer: start.consumer,
                facing: facing,
                zoom: start.settings.zoom > 0 ? start.settings.zoom : 1,
                zoomPresets: capabilities.zoomPresets,
                centerStage: start.settings.centerStage && capabilities.centerStageAvailable,
                centerStageAvailable: capabilities.centerStageAvailable,
                frontAvailable: capabilities.frontAvailable,
                isStreaming: false
            )
            pipeline.setZoom(current.zoom)
            pipeline.setCenterStage(current.centerStage)
            self.pipeline = pipeline
            state = current
            await report(.starting)

            let sender = try await openMedia(id: id, token: token)
            guard token == generation else {
                sender.close()
                log.info("Camera start superseded while opening the media connection")
                return
            }
            let slot = slot
            let encoder = try H264Encoder(
                width: size.width,
                height: size.height,
                fps: Int(format.fps),
                bitrateKbps: Int(format.bitrateKbps)
            ) { packet in
                slot.enqueue(packet)
            }
            self.sender = sender
            slot.set(sender)
            self.encoder = encoder
            pipeline.setFrameHandler { image, time in
                encoder.encode(image, at: time)
            }
            pipeline.setReconfiguredHandler {
                encoder.requestKeyFrame()
            }
            // The rotation coordinator follows the phone's physical orientation, also while the
            // interface stays in portrait.
            UIDevice.current.beginGeneratingDeviceOrientationNotifications()
            pipeline.start()
            UIApplication.shared.isIdleTimerDisabled = true
            current.isStreaming = true
            state = current
            watchDevice()
            await report(.streaming)
            log.info("Camera streaming \(size.width)x\(size.height) at \(format.fps) fps, \(format.bitrateKbps) kbps")
        } catch {
            guard token == generation else {
                return
            }
            log.error("Starting the camera failed: \(String(describing: error))")
            stop(reason: "start failed", sendStop: false, reportStopped: false)
            await report(.failed, reason: String(describing: error), streamID: id)
        }
    }

    /// Opens a media connection and sends `MediaStreamOpen`; the sender is started.
    private func openMedia(id: String, token: Int) async throws -> MediaSender {
        let connection = try await wifi.openStream()
        var open = Fenpo_V1_MediaStreamOpen()
        open.streamID = id
        open.kind = .camera
        open.video = format
        open.audio = .unspecified
        var sequencer = EnvelopeSequencer()
        let first = sequencer.envelope(.mediaStreamOpen(open))
        do {
            try await connection.sendRaw(Data(try EnvelopeFraming.frame(try first.serializedBytes())))
        } catch {
            await connection.cancel()
            throw error
        }
        let sender = MediaSender(connection: connection) { [weak self] reason in
            Task { @MainActor in
                await self?.mediaLost(reason: reason, id: id, token: token)
            }
        }
        sender.start()
        log.info("Media connection open to \(connection.endpoint.description)")
        return sender
    }

    /// The media connection broke while the camera runs: open a new one for the same stream.
    private func mediaLost(reason: String, id: String, token: Int) async {
        guard token == generation, streamID == id, state != nil else {
            return
        }
        slot.set(nil)
        sender = nil
        for attempt in 1...Self.reconnectAttempts {
            log.info("Reopening the media connection, attempt \(attempt)")
            do {
                let sender = try await openMedia(id: id, token: token)
                guard token == generation else {
                    sender.close()
                    return
                }
                self.sender = sender
                slot.set(sender)
                encoder?.requestKeyFrame()
                return
            } catch {
                log.error("Reopening failed: \(String(describing: error))")
            }
            try? await Task.sleep(for: .seconds(1))
            guard token == generation else {
                return
            }
        }
        stop(reason: "media connection lost: \(reason)", sendStop: false, reportStopped: false)
        await report(.failed, reason: "The media connection to the PC was lost", streamID: id)
    }

    /// Logs memory warnings and heat while streaming; a hot phone gets half the bitrate so it can
    /// keep going instead of being stopped by iOS.
    private func watchDevice() {
        let center = NotificationCenter.default
        observers = [
            center.addObserver(forName: UIApplication.didReceiveMemoryWarningNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    self?.log.error("Memory warning while the camera runs")
                }
            },
            center.addObserver(forName: ProcessInfo.thermalStateDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    self?.thermalStateChanged()
                }
            },
        ]
        thermalStateChanged()
    }

    private func thermalStateChanged() {
        let thermal = ProcessInfo.processInfo.thermalState
        log.info("Thermal state \(thermal.rawValue)")
        let full = Int(format.bitrateKbps)
        encoder?.setBitrate(kbps: thermal.rawValue >= ProcessInfo.ThermalState.serious.rawValue ? full / 2 : full)
    }

    /// Turns everything off. Also cancels a start in progress.
    /// - Parameters:
    ///   - reason: For the log.
    ///   - sendStop: Also send `CameraStop` (the phone ended it).
    ///   - reportStopped: Report `STOPPED` when something was running.
    private func stop(reason: String, sendStop: Bool, reportStopped: Bool = true) {
        generation += 1
        let wasRunning = pipeline != nil || sender != nil
        if encoder != nil {
            UIDevice.current.endGeneratingDeviceOrientationNotifications()
        }
        for observer in observers {
            NotificationCenter.default.removeObserver(observer)
        }
        observers = []
        let oldStreamID = streamID
        streamID = nil
        pipeline?.stop()
        pipeline = nil
        slot.set(nil)
        sender?.close()
        sender = nil
        encoder = nil
        state = nil
        UIApplication.shared.isIdleTimerDisabled = false
        guard wasRunning || sendStop else {
            return
        }
        log.info("Camera stopped: \(reason)")
        guard reportStopped else {
            return
        }
        Task {
            if sendStop {
                _ = try? await wifi.send(.cameraStop(Fenpo_V1_CameraStop()))
            }
            await report(.stopped, streamID: oldStreamID)
        }
    }

    private func apply(_ settings: Fenpo_V1_CameraSettings, consumer: String) {
        guard var current = state else {
            return
        }
        if !consumer.isEmpty {
            current.consumer = consumer
        }
        state = current
        if settings.facing != .unspecified, settings.facing != current.facing {
            flip()
        }
        if settings.zoom > 0 {
            setZoom(settings.zoom)
        }
        if settings.centerStage != current.centerStage {
            setCenterStage(settings.centerStage)
        }
        Task {
            await report(.streaming)
        }
    }

    private func currentSettings() -> Fenpo_V1_CameraSettings {
        var settings = Fenpo_V1_CameraSettings()
        settings.facing = state?.facing ?? .back
        settings.zoom = state?.zoom ?? 1
        settings.microphone = false
        settings.centerStage = state?.centerStage ?? false
        return settings
    }

    private func sendSettingsChange() {
        var change = Fenpo_V1_CameraSettingsChange()
        change.settings = currentSettings()
        change.consumer = state?.consumer ?? ""
        Task {
            _ = try? await wifi.send(.cameraSettings(change))
            await report(.streaming)
        }
    }

    private func report(_ cameraState: Fenpo_V1_CameraState, reason: String = "", streamID: String? = nil) async {
        var status = Fenpo_V1_CameraStatus()
        status.state = cameraState
        status.streamID = streamID ?? self.streamID ?? ""
        status.settings = currentSettings()
        status.video = format
        status.zoomPresets = state?.zoomPresets ?? []
        status.frontAvailable = state?.frontAvailable ?? true
        status.reason = reason
        do {
            try await wifi.send(.cameraStatus(status))
        } catch {
            log.error("Camera status not sent: \(String(describing: error))")
        }
    }

    private func notifyWaiting(consumer: String) {
        let content = UNMutableNotificationContent()
        content.title = String(localized: "camera.notification.title")
        content.body = consumer.isEmpty ? String(localized: "camera.notification.body") : String(localized: "camera.notification.body.consumer \(consumer)")
        content.interruptionLevel = .active
        let request = UNNotificationRequest(identifier: "com.fenpo.camera", content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }
}
#endif
