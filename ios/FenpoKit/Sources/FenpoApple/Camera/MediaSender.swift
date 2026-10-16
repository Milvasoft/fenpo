#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import Synchronization

/// Writes `[uint32 length][StreamPacket]` frames to a media connection in order, dropping video
/// when the connection falls behind (`MediaPacketQueue`).
final class MediaSender: Sendable {
    private let connection: TLSControlConnection
    private let queue = Mutex(MediaPacketQueue<Data>())
    private let wake: AsyncStream<Void>.Continuation
    private let signals: AsyncStream<Void>
    private let worker = Mutex<Task<Void, Never>?>(nil)

    /// Creates a sender for an open media connection.
    /// - Parameter connection: Connection that already carried `MediaStreamOpen`.
    init(connection: TLSControlConnection) {
        self.connection = connection
        let (signals, wake) = AsyncStream<Void>.makeStream(bufferingPolicy: .bufferingNewest(1))
        self.signals = signals
        self.wake = wake
    }

    /// Video frames dropped so far.
    var droppedVideoFrames: Int {
        queue.withLock { $0.droppedVideoFrames }
    }

    /// Starts writing.
    func start() {
        let signals = signals
        let task = Task.detached { [self] in
            for await _ in signals {
                while let next = self.queue.withLock({ $0.pop() }) {
                    do {
                        try await self.connection.sendRaw(next)
                    } catch {
                        return
                    }
                }
            }
        }
        worker.withLock { $0 = task }
    }

    /// Queues a packet; safe from any thread.
    /// - Parameter packet: Encoded packet.
    func enqueue(_ packet: Fenpo_V1_StreamPacket) {
        guard let bytes = try? EnvelopeFraming.frame(try packet.serializedBytes()) else {
            return
        }
        let entry = MediaPacketQueue<Data>.Entry(
            packet: Data(bytes),
            isVideo: packet.track == .video,
            isKeyFrame: packet.keyFrame,
            isCodecConfig: packet.codecConfig
        )
        queue.withLock { $0.push(entry) }
        wake.yield()
    }

    /// Stops writing and closes the connection.
    func close() {
        wake.finish()
        worker.withLock { $0?.cancel() }
        let connection = connection
        Task {
            await connection.cancel()
        }
    }
}
#endif
