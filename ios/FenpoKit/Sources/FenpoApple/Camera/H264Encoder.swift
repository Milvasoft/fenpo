#if os(iOS)
import CoreMedia
import FenpoCore
import FenpoProtocol
import Foundation
import Synchronization
import VideoToolbox

/// Hardware H.264 for the media connection: real time, no B-frames, a key frame about every two
/// seconds, Annex-B output with SPS/PPS sent as `codec_config` before every key frame
/// (protocol/README.md, "Video").
final class H264Encoder: @unchecked Sendable {
    /// Receives encoded packets on the encoder's thread, in order.
    typealias Output = @Sendable (Fenpo_V1_StreamPacket) -> Void

    private let session: VTCompressionSession
    private let output: Output
    private let forceKeyFrame = Mutex(true)

    /// Creates an encoder.
    /// - Parameters:
    ///   - width: Encoded width.
    ///   - height: Encoded height.
    ///   - fps: Expected frame rate.
    ///   - bitrateKbps: Average bitrate.
    ///   - output: Receives packets.
    init(width: Int, height: Int, fps: Int, bitrateKbps: Int, output: @escaping Output) throws {
        var created: VTCompressionSession?
        let status = VTCompressionSessionCreate(
            allocator: nil,
            width: Int32(width),
            height: Int32(height),
            codecType: kCMVideoCodecType_H264,
            encoderSpecification: nil,
            imageBufferAttributes: nil,
            compressedDataAllocator: nil,
            outputCallback: nil,
            refcon: nil,
            compressionSessionOut: &created
        )
        guard status == noErr, let created else {
            throw CameraError.encoderUnavailable(status)
        }
        session = created
        self.output = output

        VTSessionSetProperty(created, key: kVTCompressionPropertyKey_RealTime, value: kCFBooleanTrue)
        VTSessionSetProperty(created, key: kVTCompressionPropertyKey_ProfileLevel, value: kVTProfileLevel_H264_Main_AutoLevel)
        VTSessionSetProperty(created, key: kVTCompressionPropertyKey_AllowFrameReordering, value: kCFBooleanFalse)
        VTSessionSetProperty(created, key: kVTCompressionPropertyKey_AverageBitRate, value: NSNumber(value: bitrateKbps * 1000))
        VTSessionSetProperty(created, key: kVTCompressionPropertyKey_ExpectedFrameRate, value: NSNumber(value: fps))
        VTSessionSetProperty(created, key: kVTCompressionPropertyKey_MaxKeyFrameIntervalDuration, value: NSNumber(value: 2))
        VTCompressionSessionPrepareToEncodeFrames(created)
    }

    deinit {
        VTCompressionSessionInvalidate(session)
    }

    /// Changes the bitrate, for example when the connection falls behind.
    /// - Parameter kbps: New average bitrate.
    func setBitrate(kbps: Int) {
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_AverageBitRate, value: NSNumber(value: kbps * 1000))
    }

    /// The next frame becomes a key frame (after `RequestKeyFrame` or a lost connection).
    func requestKeyFrame() {
        forceKeyFrame.withLock { $0 = true }
    }

    /// Encodes one camera frame.
    /// - Parameter sampleBuffer: Frame from the capture output.
    func encode(_ sampleBuffer: CMSampleBuffer) {
        guard let image = CMSampleBufferGetImageBuffer(sampleBuffer) else {
            return
        }
        let presentationTime = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        let key = forceKeyFrame.withLock { value in
            defer { value = false }
            return value
        }
        let properties: CFDictionary? = key ? [kVTEncodeFrameOptionKey_ForceKeyFrame: kCFBooleanTrue] as CFDictionary : nil
        VTCompressionSessionEncodeFrame(
            session,
            imageBuffer: image,
            presentationTimeStamp: presentationTime,
            duration: .invalid,
            frameProperties: properties,
            infoFlagsOut: nil
        ) { [weak self] status, _, buffer in
            guard status == noErr, let buffer else {
                return
            }
            self?.emit(buffer)
        }
    }

    private func emit(_ buffer: CMSampleBuffer) {
        let attachments = CMSampleBufferGetSampleAttachmentsArray(buffer, createIfNecessary: false) as? [[CFString: Any]]
        let isKeyFrame = !((attachments?.first?[kCMSampleAttachmentKey_NotSync] as? Bool) ?? false)
        let seconds = CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(buffer))
        let ptsMicroseconds = seconds.isFinite ? UInt64(max(0, seconds * 1_000_000)) : 0

        var headerLength = 4
        if isKeyFrame, let format = CMSampleBufferGetFormatDescription(buffer) {
            let (config, length) = Self.parameterSets(of: format)
            headerLength = length
            if !config.isEmpty {
                var packet = Fenpo_V1_StreamPacket()
                packet.track = .video
                packet.ptsUs = ptsMicroseconds
                packet.keyFrame = true
                packet.codecConfig = true
                packet.data = Data(config)
                output(packet)
            }
        }

        guard let block = CMSampleBufferGetDataBuffer(buffer) else {
            return
        }
        let length = CMBlockBufferGetDataLength(block)
        var avcc = [UInt8](repeating: 0, count: length)
        guard CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: length, destination: &avcc) == kCMBlockBufferNoErr else {
            return
        }

        var packet = Fenpo_V1_StreamPacket()
        packet.track = .video
        packet.ptsUs = ptsMicroseconds
        packet.keyFrame = isKeyFrame
        packet.data = Data(AnnexB.from(avcc: avcc, lengthSize: headerLength))
        output(packet)
    }

    /// SPS and PPS in Annex-B, and the AVCC NAL length size.
    private static func parameterSets(of format: CMFormatDescription) -> ([UInt8], Int) {
        var count = 0
        var headerLength: Int32 = 4
        guard CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
            format,
            parameterSetIndex: 0,
            parameterSetPointerOut: nil,
            parameterSetSizeOut: nil,
            parameterSetCountOut: &count,
            nalUnitHeaderLengthOut: &headerLength
        ) == noErr else {
            return ([], 4)
        }
        var config: [UInt8] = []
        for index in 0..<count {
            var pointer: UnsafePointer<UInt8>?
            var size = 0
            if CMVideoFormatDescriptionGetH264ParameterSetAtIndex(
                format,
                parameterSetIndex: index,
                parameterSetPointerOut: &pointer,
                parameterSetSizeOut: &size,
                parameterSetCountOut: nil,
                nalUnitHeaderLengthOut: nil
            ) == noErr, let pointer {
                config += AnnexB.startCode
                config += Array(UnsafeBufferPointer(start: pointer, count: size))
            }
        }
        return (config, Int(headerLength))
    }
}

/// Why the camera could not start.
enum CameraError: Error {
    case encoderUnavailable(OSStatus)
    case noCamera
    case cannotAddInput
}
#endif
