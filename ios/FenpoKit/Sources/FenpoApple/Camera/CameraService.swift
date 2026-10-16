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
    private var format = Fenpo_V1_VideoFormat()
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
            stop(sendStop: false)
        case .cameraSettings(let change):
            apply(change.settings, consumer: change.consumer)
        case .requestKeyFrame(let request):
            if request.streamID == streamID {
                encoder?.requestKeyFrame()
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

    /// Stops the camera from the phone (the close button).
    public func stopFromPhone() {
        stop(sendStop: true)
    }

    /// Zoom from the lens buttons.
    /// - Parameter ratio: Display zoom ratio.
    public func setZoom(_ ratio: Float) {
        guard var current = state else {
            return
        }
        pipeline?.setZoom(ratio)
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
        current.centerStage = isOn
        state = current
        sendSettingsChange()
    }

    // MARK: - Private

    private func begin(_ start: Fenpo_V1_CameraStart) async {
        request = start
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

        stop(sendStop: false)
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

            let connection = try await wifi.openStream()
            var open = Fenpo_V1_MediaStreamOpen()
            open.streamID = id
            open.kind = .camera
            open.video = format
            open.audio = .unspecified
            var sequencer = EnvelopeSequencer()
            let first = sequencer.envelope(.mediaStreamOpen(open))
            try await connection.sendRaw(Data(try EnvelopeFraming.frame(try first.serializedBytes())))

            let sender = MediaSender(connection: connection)
            sender.start()
            let encoder = try H264Encoder(
                width: size.width,
                height: size.height,
                fps: Int(format.fps),
                bitrateKbps: Int(format.bitrateKbps)
            ) { packet in
                sender.enqueue(packet)
            }
            self.sender = sender
            self.encoder = encoder
            pipeline.setFrameHandler { buffer in
                encoder.encode(buffer)
            }
            pipeline.start()
            UIApplication.shared.isIdleTimerDisabled = true
            current.isStreaming = true
            state = current
            await report(.streaming)
            log.info("Camera streaming \(size.width)x\(size.height) at \(format.fps) fps, \(format.bitrateKbps) kbps")
        } catch {
            log.error("Starting the camera failed: \(String(describing: error))")
            stop(sendStop: false)
            await report(.failed, reason: String(describing: error))
        }
    }

    private func stop(sendStop: Bool) {
        let wasRunning = pipeline != nil
        pipeline?.stop()
        pipeline = nil
        sender?.close()
        sender = nil
        encoder = nil
        state = nil
        UIApplication.shared.isIdleTimerDisabled = false
        guard wasRunning || sendStop else {
            return
        }
        Task {
            if sendStop {
                _ = try? await wifi.send(.cameraStop(Fenpo_V1_CameraStop()))
            }
            await report(.stopped)
            streamID = nil
        }
        log.info("Camera stopped")
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

    private func report(_ cameraState: Fenpo_V1_CameraState, reason: String = "") async {
        var status = Fenpo_V1_CameraStatus()
        status.state = cameraState
        status.streamID = streamID ?? ""
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
