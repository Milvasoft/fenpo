#if os(iOS)
@preconcurrency import AVFoundation
import FenpoProtocol
import Foundation
import Synchronization

/// The camera side of Fenpo Kamera: an `AVCaptureSession` that hands every frame to a handler on
/// its own queue. Frames are turned upright for however the phone is held (the rotation
/// coordinator's horizon-level angle) and always leave as the same landscape size, like a webcam:
/// a portrait picture is cropped to its centered 16:9 part (`FrameFitter`).
final class CapturePipeline: NSObject, @unchecked Sendable {
    /// Receives each frame at the output size, with its presentation time.
    typealias FrameHandler = @Sendable (CVPixelBuffer, CMTime) -> Void

    /// The session, for the preview layer.
    let session = AVCaptureSession()

    // Every frame creates Core Image and Core Media objects; draining after each one keeps the
    // camera's own buffers free and memory flat.
    private let queue = DispatchQueue(label: "com.fenpo.camera", autoreleaseFrequency: .workItem)
    private let output = AVCaptureVideoDataOutput()
    private var input: AVCaptureDeviceInput?
    private let handler = Mutex<FrameHandler?>(nil)
    private let fitter = Mutex<FrameFitter?>(nil)
    private var rotationCoordinator: AVCaptureDevice.RotationCoordinator?
    private var rotationObservation: NSKeyValueObservation?
    private var notificationTokens: [NSObjectProtocol] = []
    private let lastAngle = Mutex<CGFloat>(-1)
    private let droppedFrames = Mutex(0)
    private let log = FenpoLog("Camera")

    /// What the current camera can do.
    struct Capabilities: Sendable {
        let zoomPresets: [Float]
        let frontAvailable: Bool
        let centerStageAvailable: Bool
    }

    /// Sets who receives frames.
    /// - Parameter frameHandler: Called on the capture queue.
    func setFrameHandler(_ frameHandler: FrameHandler?) {
        handler.withLock { $0 = frameHandler }
    }

    /// Picks the camera and the size; call before `start()` and again to flip.
    /// - Parameters:
    ///   - facing: Back or front.
    ///   - fullHD: 1920 x 1080 when true, 1280 x 720 otherwise.
    @MainActor
    func configure(facing: Fenpo_V1_CameraFacing, fullHD: Bool) throws -> Capabilities {
        guard let device = Self.device(facing: facing) else {
            throw CameraError.noCamera
        }
        let newInput = try AVCaptureDeviceInput(device: device)

        session.beginConfiguration()
        defer {
            session.commitConfiguration()
        }
        session.sessionPreset = fullHD && session.canSetSessionPreset(.hd1920x1080) ? .hd1920x1080 : .hd1280x720
        if let input {
            session.removeInput(input)
        }
        guard session.canAddInput(newInput) else {
            throw CameraError.cannotAddInput
        }
        session.addInput(newInput)
        input = newInput

        if !session.outputs.contains(output), session.canAddOutput(output) {
            output.alwaysDiscardsLateVideoFrames = true
            output.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange]
            output.setSampleBufferDelegate(self, queue: queue)
            session.addOutput(output)
        }
        if let connection = output.connection(with: .video) {
            if connection.isVideoMirroringSupported {
                connection.automaticallyAdjustsVideoMirroring = false
                connection.isVideoMirrored = false
            }
        }
        let size = dimensions
        fitter.withLock { $0 = FrameFitter(width: size.width, height: size.height) }
        followRotation(of: device)

        let ultraWide = [.builtInTripleCamera, .builtInDualWideCamera].contains(device.deviceType)
        let presets: [Float] = facing == .front ? [1] : (ultraWide ? [0.5, 1, 2] : [1, 2])
        return Capabilities(
            zoomPresets: presets,
            frontAvailable: Self.device(facing: .front) != nil,
            centerStageAvailable: device.activeFormat.isCenterStageSupported
        )
    }

    /// Output size for the current preset, landscape.
    var dimensions: (width: Int, height: Int) {
        session.sessionPreset == .hd1920x1080 ? (1920, 1080) : (1280, 720)
    }

    /// Sets the zoom as shown to people (0.5, 1, 2 ...).
    /// - Parameter ratio: Display zoom ratio.
    func setZoom(_ ratio: Float) {
        guard let device = input?.device, ratio > 0 else {
            return
        }
        let multiplier = Float(device.displayVideoZoomFactorMultiplier)
        let factor = CGFloat(ratio / max(multiplier, 0.01))
        let clamped = min(max(factor, device.minAvailableVideoZoomFactor), min(device.maxAvailableVideoZoomFactor, 10))
        do {
            try device.lockForConfiguration()
            device.videoZoomFactor = clamped
            device.unlockForConfiguration()
        } catch {
            return
        }
    }

    /// Turns Center Stage on or off where the camera supports it.
    /// - Parameter isOn: Desired state.
    func setCenterStage(_ isOn: Bool) {
        guard input?.device.activeFormat.isCenterStageSupported == true else {
            return
        }
        AVCaptureDevice.centerStageControlMode = .cooperative
        AVCaptureDevice.isCenterStageEnabled = isOn
    }

    /// Keeps the output upright: the coordinator reports the angle that levels the picture with the
    /// horizon, for the back and the front camera, as the phone turns.
    @MainActor
    private func followRotation(of device: AVCaptureDevice) {
        rotationObservation = nil
        let coordinator = AVCaptureDevice.RotationCoordinator(device: device, previewLayer: nil)
        rotationCoordinator = coordinator
        apply(rotationAngle: coordinator.videoRotationAngleForHorizonLevelCapture)
        rotationObservation = coordinator.observe(\.videoRotationAngleForHorizonLevelCapture, options: [.new]) { [weak self] _, change in
            guard let angle = change.newValue else {
                return
            }
            self?.apply(rotationAngle: angle)
        }
    }

    private func apply(rotationAngle angle: CGFloat) {
        let changed = lastAngle.withLock { last in
            defer { last = angle }
            return last != angle
        }
        if changed {
            log.info("Capture rotation \(Int(angle))°")
        }
        let output = output
        queue.async {
            guard let connection = output.connection(with: .video), connection.isVideoRotationAngleSupported(angle) else {
                return
            }
            connection.videoRotationAngle = angle
        }
    }

    /// Starts capturing (off the main thread).
    func start() {
        watchSession()
        let session = session
        queue.async {
            session.startRunning()
        }
    }

    /// Stops capturing.
    func stop() {
        for token in notificationTokens {
            NotificationCenter.default.removeObserver(token)
        }
        notificationTokens = []
        setFrameHandler(nil)
        let session = session
        queue.async {
            session.stopRunning()
        }
    }

    /// Logs interruptions and errors of the capture session, and restarts it after a runtime error
    /// (for example when the media server resets), so a frozen picture shows up in the log.
    private func watchSession() {
        let center = NotificationCenter.default
        let session = session
        let queue = queue
        let log = log
        notificationTokens = [
            center.addObserver(forName: AVCaptureSession.wasInterruptedNotification, object: session, queue: nil) { notification in
                let reason = notification.userInfo?[AVCaptureSessionInterruptionReasonKey] as? Int ?? -1
                log.error("Capture interrupted, reason \(reason)")
            },
            center.addObserver(forName: AVCaptureSession.interruptionEndedNotification, object: session, queue: nil) { _ in
                log.info("Capture interruption ended")
            },
            center.addObserver(forName: AVCaptureSession.runtimeErrorNotification, object: session, queue: nil) { notification in
                let error = notification.userInfo?[AVCaptureSessionErrorKey] as? NSError
                log.error("Capture runtime error \(error?.code ?? 0); restarting")
                queue.async {
                    session.startRunning()
                }
            },
        ]
    }

    private static func device(facing: Fenpo_V1_CameraFacing) -> AVCaptureDevice? {
        if facing == .front {
            return AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .front)
        }
        let types: [AVCaptureDevice.DeviceType] = [.builtInTripleCamera, .builtInDualWideCamera, .builtInDualCamera, .builtInWideAngleCamera]
        return AVCaptureDevice.DiscoverySession(deviceTypes: types, mediaType: .video, position: .back).devices.first
    }
}

extension CapturePipeline: AVCaptureVideoDataOutputSampleBufferDelegate {
    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        autoreleasepool {
            guard
                let handler = handler.withLock({ $0 }),
                let image = CMSampleBufferGetImageBuffer(sampleBuffer),
                let fitted = fitter.withLock({ $0 })?.fit(image)
            else {
                return
            }
            handler(fitted, CMSampleBufferGetPresentationTimeStamp(sampleBuffer))
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didDrop sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        let count = droppedFrames.withLock { count in
            count += 1
            return count
        }
        if count == 1 || count % 100 == 0 {
            let reason = CMGetAttachment(sampleBuffer, key: kCMSampleBufferAttachmentKey_DroppedFrameReason, attachmentModeOut: nil) as? String ?? "unknown"
            log.info("Capture dropped a late frame (\(reason)), \(count) so far")
        }
    }
}
#endif
