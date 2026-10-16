#if os(iOS)
@preconcurrency import AVFoundation
import FenpoProtocol
import Foundation
import Synchronization

/// The camera side of Fenpo Kamera: an `AVCaptureSession` that hands every frame to a handler on
/// its own queue. Landscape output, like a webcam.
final class CapturePipeline: NSObject, @unchecked Sendable {
    /// The session, for the preview layer.
    let session = AVCaptureSession()

    private let queue = DispatchQueue(label: "com.fenpo.camera")
    private let output = AVCaptureVideoDataOutput()
    private var input: AVCaptureDeviceInput?
    private let handler = Mutex<(@Sendable (CMSampleBuffer) -> Void)?>(nil)

    /// What the current camera can do.
    struct Capabilities: Sendable {
        let zoomPresets: [Float]
        let frontAvailable: Bool
        let centerStageAvailable: Bool
    }

    /// Sets who receives frames.
    /// - Parameter frameHandler: Called on the capture queue.
    func setFrameHandler(_ frameHandler: (@Sendable (CMSampleBuffer) -> Void)?) {
        handler.withLock { $0 = frameHandler }
    }

    /// Picks the camera and the size; call before `start()` and again to flip.
    /// - Parameters:
    ///   - facing: Back or front.
    ///   - fullHD: 1920 x 1080 when true, 1280 x 720 otherwise.
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
            if connection.isVideoRotationAngleSupported(0) {
                connection.videoRotationAngle = 0
            }
            if connection.isVideoMirroringSupported {
                connection.automaticallyAdjustsVideoMirroring = false
                connection.isVideoMirrored = false
            }
        }

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

    /// Starts capturing (off the main thread).
    func start() {
        let session = session
        queue.async {
            session.startRunning()
        }
    }

    /// Stops capturing.
    func stop() {
        setFrameHandler(nil)
        let session = session
        queue.async {
            session.stopRunning()
        }
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
        handler.withLock { $0 }?(sampleBuffer)
    }
}
#endif
