#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import Synchronization

/// Writes `[uint32 length][StreamPacket]` frames to a media connection in order, dropping video
/// when the connection falls behind (`MediaPacketQueue`). It also reads the connection, only to
/// notice when the PC closes it, and reports the first failure through `onClosed`.
final class MediaSender: Sendable {
    /// Counters for the log.
    struct Stats: Sendable {
        var packets = 0
        var bytes = 0
    }

    private let connection: TLSControlConnection
    private let queue = Mutex(MediaPacketQueue<Data>())
    private let wake: AsyncStream<Void>.Continuation
    private let signals: AsyncStream<Void>
    private let tasks = Mutex<[Task<Void, Never>]>([])
    private let finished = Mutex(false)
    private let stats = Mutex(Stats())
    private let onClosed: @Sendable (String) -> Void
    private let log = FenpoLog("Camera")

    /// Creates a sender for an open media connection.
    /// - Parameters:
    ///   - connection: Connection that already carried `MediaStreamOpen`.
    ///   - onClosed: Called once, from any thread, when the connection fails or the PC closes it;
    ///     not called after `close()`.
    init(connection: TLSControlConnection, onClosed: @escaping @Sendable (String) -> Void) {
        self.connection = connection
        self.onClosed = onClosed
        let (signals, wake) = AsyncStream<Void>.makeStream(bufferingPolicy: .bufferingNewest(1))
        self.signals = signals
        self.wake = wake
    }

    /// Video frames dropped so far.
    var droppedVideoFrames: Int {
        queue.withLock { $0.droppedVideoFrames }
    }

    /// Packets and bytes written so far.
    var written: Stats {
        stats.withLock { $0 }
    }

    /// Starts writing, and watching for the PC closing the connection.
    func start() {
        let signals = signals
        let writer = Task.detached { [self] in
            for await _ in signals {
                while let next = self.queue.withLock({ $0.pop() }) {
                    do {
                        try await self.connection.sendRaw(next)
                    } catch {
                        self.fail("write failed: \(String(describing: error))")
                        return
                    }
                    let first = self.stats.withLock { stats in
                        stats.packets += 1
                        stats.bytes += next.count
                        return stats.packets == 1
                    }
                    if first {
                        self.log.info("First media packet written, \(next.count) bytes")
                    }
                }
            }
        }
        let reader = Task.detached { [self] in
            do {
                while !Task.isCancelled {
                    guard try await self.connection.receiveRaw() != nil else {
                        self.fail("the PC closed the media connection")
                        return
                    }
                }
            } catch {
                self.fail("read failed: \(String(describing: error))")
            }
        }
        tasks.withLock { $0 = [writer, reader] }
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
        guard finish() else {
            return
        }
        let stats = written
        log.info("Media connection closed by the iPhone after \(stats.packets) packets, \(stats.bytes) bytes, \(self.droppedVideoFrames) frames dropped")
        shutDown()
    }

    private func fail(_ reason: String) {
        guard finish() else {
            return
        }
        let stats = written
        log.error("Media connection lost (\(reason)) after \(stats.packets) packets, \(stats.bytes) bytes")
        shutDown()
        onClosed(reason)
    }

    /// Marks the sender finished; true only for the first caller.
    private func finish() -> Bool {
        finished.withLock { done in
            defer { done = true }
            return !done
        }
    }

    private func shutDown() {
        wake.finish()
        let tasks = tasks.withLock { $0 }
        for task in tasks {
            task.cancel()
        }
        let connection = connection
        Task {
            await connection.cancel()
        }
    }
}

/// The media sender the encoder writes to; swapped when the media connection is reopened.
final class MediaSenderSlot: Sendable {
    private let current = Mutex<MediaSender?>(nil)

    /// Sets the sender; `nil` drops packets.
    /// - Parameter sender: New sender.
    func set(_ sender: MediaSender?) {
        current.withLock { $0 = sender }
    }

    /// Queues a packet on the current sender.
    /// - Parameter packet: Encoded packet.
    func enqueue(_ packet: Fenpo_V1_StreamPacket) {
        current.withLock { $0 }?.enqueue(packet)
    }
}
#endif
