#if os(iOS)
import CoreMedia
import CoreVideo
import FenpoCore
import FenpoProtocol
import Foundation
import Synchronization

/// The iPhone's screen on the PC (protocol/README.md, "Screen mirroring from the iPhone"), run by the
/// broadcast upload extension: it has its own Wi-Fi channel, opens a SCREEN media connection when
/// the first frame shows the size, and reports `STREAMING` without a `ScreenStart`. The PC cannot
/// control the iPhone. Kept small: the extension may use about 50 MB.
public final class ScreenBroadcaster: @unchecked Sendable {
    /// Largest side sent to the PC (protocol/README.md, "Video").
    private static let maxSide = 1920
    fileprivate static let fps: Int32 = 30
    private static let bitrateKbps = 6000

    private struct Running {
        var encoder: H264Encoder?
        var isOpening = false
        var pacer = FramePacer(fps: Double(ScreenBroadcaster.fps))
    }

    private let state = Mutex(Running())
    private let slot = MediaSenderSlot()
    private let onStop: @Sendable (String) -> Void
    private let log = FenpoLog("Screen")
    @MainActor private var channel: ScreenChannel?

    /// Creates a broadcaster.
    /// - Parameter onStop: Called with a reason when the broadcast has to end (the PC stopped it,
    ///   the PC cannot show it, the connection broke).
    public init(onStop: @escaping @Sendable (String) -> Void) {
        self.onStop = onStop
    }

    /// The broadcast started: connect to the PC.
    public func start() {
        guard FeatureAccess.isAvailable(.screenMirror) else {
            onStop("unsupported")
            return
        }
        Task { @MainActor in
            let channel = ScreenChannel { [weak self] reason in
                self?.stop(reason)
            } keyFrame: { [weak self] in
                self?.state.withLock { $0.encoder }?.requestKeyFrame()
            }
            self.channel = channel
            do {
                try await channel.connect()
                log.info("Broadcast connected")
            } catch is ScreenChannel.Unsupported {
                log.error("The PC does not show the iPhone's screen")
                stop("unsupported")
            } catch {
                log.error("Broadcast cannot reach the PC: \(String(describing: error))")
                stop("unreachable")
            }
        }
    }

    /// A screen frame from ReplayKit; frames above 30 a second are skipped.
    /// - Parameter sampleBuffer: Video sample.
    public func process(_ sampleBuffer: CMSampleBuffer) {
        guard let image = CMSampleBufferGetImageBuffer(sampleBuffer) else {
            return
        }
        let time = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        let action: (encoder: H264Encoder?, open: Bool) = state.withLock { running in
            if let encoder = running.encoder {
                guard running.pacer.accept(CMTimeGetSeconds(time)) else {
                    return (nil, false)
                }
                return (encoder, false)
            }
            guard !running.isOpening else {
                return (nil, false)
            }
            running.isOpening = true
            return (nil, true)
        }
        if let encoder = action.encoder {
            encoder.encode(image, at: time)
        } else if action.open {
            let size = Self.outputSize(width: CVPixelBufferGetWidth(image), height: CVPixelBufferGetHeight(image))
            Task { @MainActor in
                await self.openMedia(width: size.width, height: size.height)
            }
        }
    }

    /// The broadcast ended on the iPhone.
    public func finish() {
        Task { @MainActor in
            await channel?.report(.stopped)
            close()
        }
    }

    /// The largest even size with the frame's aspect whose long side is at most 1920.
    static func outputSize(width: Int, height: Int) -> (width: Int, height: Int) {
        let longest = max(width, height)
        let scale = longest > maxSide ? Double(maxSide) / Double(longest) : 1
        func even(_ value: Double) -> Int {
            max(2, Int(value / 2) * 2)
        }
        return (even(Double(width) * scale), even(Double(height) * scale))
    }

    @MainActor
    private func openMedia(width: Int, height: Int) async {
        guard let channel else {
            return
        }
        var format = Fenpo_V1_VideoFormat()
        format.codec = .h264
        format.width = UInt32(width)
        format.height = UInt32(height)
        format.fps = UInt32(Self.fps)
        format.bitrateKbps = UInt32(Self.bitrateKbps)
        do {
            let streamID = UUID().uuidString
            let sender = try await channel.openMedia(streamID: streamID, format: format) { [weak self] reason in
                self?.stop("media connection lost: \(reason)")
            }
            slot.set(sender)
            let slot = slot
            let encoder = try H264Encoder(width: width, height: height, fps: Int(Self.fps), bitrateKbps: Self.bitrateKbps) { packet in
                slot.enqueue(packet)
            }
            state.withLock { running in
                running.encoder = encoder
                running.isOpening = false
            }
            await channel.report(.streaming, streamID: streamID, format: format)
            log.info("Broadcast streaming \(width)x\(height)")
        } catch {
            log.error("Broadcast media failed: \(String(describing: error))")
            stop("media")
        }
    }

    private func stop(_ reason: String) {
        log.info("Broadcast stops: \(reason)")
        Task { @MainActor in
            close()
        }
        onStop(reason)
    }

    @MainActor
    private func close() {
        slot.set(nil)
        state.withLock { running in
            running.encoder = nil
        }
        channel?.close()
        channel = nil
    }
}

/// The broadcast extension's control channel: status reports, the media connection, and the PC's
/// ScreenStop, RequestKeyFrame and ScreenInput.
@MainActor
final class ScreenChannel {
    struct Unsupported: Error {}

    private let wifi = WiFiChannel(client: DataChannelClient())
    private var sender: MediaSender?
    private var streamID = ""
    private let onStop: (String) -> Void
    private let keyFrame: () -> Void

    init(onStop: @escaping (String) -> Void, keyFrame: @escaping () -> Void) {
        self.onStop = onStop
        self.keyFrame = keyFrame
        wifi.onEnvelope = { [weak self] envelope in
            self?.handle(envelope)
        }
    }

    func connect() async throws {
        try await wifi.connectIfNeeded()
        guard wifi.session?.supports(.screenMirror) == true else {
            throw Unsupported()
        }
    }

    func openMedia(streamID: String, format: Fenpo_V1_VideoFormat, onClosed: @escaping @Sendable (String) -> Void) async throws -> MediaSender {
        let connection = try await wifi.openStream()
        var open = Fenpo_V1_MediaStreamOpen()
        open.streamID = streamID
        open.kind = .screen
        open.video = format
        open.audio = .unspecified
        var sequencer = EnvelopeSequencer()
        let first = sequencer.envelope(.mediaStreamOpen(open))
        try await connection.sendRaw(Data(try EnvelopeFraming.frame(try first.serializedBytes())))
        let sender = MediaSender(connection: connection, onClosed: onClosed)
        sender.start()
        self.sender = sender
        self.streamID = streamID
        return sender
    }

    func report(_ state: Fenpo_V1_ScreenState, streamID: String? = nil, format: Fenpo_V1_VideoFormat? = nil) async {
        var status = Fenpo_V1_ScreenStatus()
        status.state = state
        status.streamID = streamID ?? self.streamID
        if let format {
            status.video = format
        }
        status.controlAvailable = false
        _ = try? await wifi.send(.screenStatus(status))
    }

    func close() {
        sender?.close()
        sender = nil
        wifi.disconnect()
    }

    private func handle(_ envelope: Fenpo_V1_Envelope) {
        switch envelope.body {
        case .screenStop:
            onStop("stopped from the PC")
        case .requestKeyFrame:
            keyFrame()
        case .screenInput:
            var error = Fenpo_V1_Error()
            error.code = .unsupported
            error.message = "The iPhone cannot be controlled from the PC"
            Task {
                _ = try? await wifi.send(.error(error), replyTo: envelope.id)
            }
        default:
            break
        }
    }
}
#endif
