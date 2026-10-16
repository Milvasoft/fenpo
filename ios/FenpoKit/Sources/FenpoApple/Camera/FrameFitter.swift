#if os(iOS)
import CoreImage
import CoreVideo
import FenpoCore
import Foundation

/// Turns upright camera frames into the fixed landscape size the PC was told about: frames of
/// that size pass through, others (portrait) are cropped to the centered 16:9 part and scaled.
/// Used on the capture queue only.
final class FrameFitter: @unchecked Sendable {
    let width: Int
    let height: Int

    private let context = CIContext(options: [.cacheIntermediates: false])
    private let colorSpace = CGColorSpace(name: CGColorSpace.itur_709)
    private var pool: CVPixelBufferPool?
    private var misses = 0
    private let log = FenpoLog("Camera")

    /// Creates a fitter for an output size.
    /// - Parameters:
    ///   - width: Output width.
    ///   - height: Output height.
    init(width: Int, height: Int) {
        self.width = width
        self.height = height
        let attributes: [String: Any] = [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange,
            kCVPixelBufferWidthKey as String: width,
            kCVPixelBufferHeightKey as String: height,
            kCVPixelBufferIOSurfacePropertiesKey as String: [String: Any](),
        ]
        CVPixelBufferPoolCreate(nil, nil, attributes as CFDictionary, &pool)
    }

    /// The frame at the output size, or `nil` when no buffer is free.
    /// - Parameter buffer: Upright camera frame.
    func fit(_ buffer: CVPixelBuffer) -> CVPixelBuffer? {
        let sourceWidth = CVPixelBufferGetWidth(buffer)
        let sourceHeight = CVPixelBufferGetHeight(buffer)
        if sourceWidth == width, sourceHeight == height {
            return buffer
        }
        guard let pool else {
            return nil
        }
        var output: CVPixelBuffer?
        guard CVPixelBufferPoolCreatePixelBuffer(nil, pool, &output) == kCVReturnSuccess, let output else {
            misses += 1
            if misses == 1 || misses % 30 == 0 {
                log.error("No buffer for a cropped frame, \(misses) so far")
            }
            return nil
        }
        let crop = FrameCrop.centered(width: sourceWidth, height: sourceHeight, targetWidth: width, targetHeight: height)
        let rect = CGRect(x: crop.x, y: crop.y, width: crop.width, height: crop.height)
        let image = CIImage(cvPixelBuffer: buffer)
            .cropped(to: rect)
            .transformed(by: CGAffineTransform(translationX: -rect.minX, y: -rect.minY))
            .transformed(by: CGAffineTransform(scaleX: CGFloat(width) / rect.width, y: CGFloat(height) / rect.height))
        context.render(image, to: output, bounds: CGRect(x: 0, y: 0, width: width, height: height), colorSpace: colorSpace)
        return output
    }
}
#endif
