/// Packets waiting for the media connection. When the connection falls behind, video frames are
/// dropped instead of queued: a new key frame makes every older video frame useless, and above
/// `maxVideoFrames` the oldest ordinary frames go. Audio and codec configuration are never dropped
/// (protocol/README.md, "Video").
public struct MediaPacketQueue<Packet: Sendable>: Sendable {
    /// What the queue needs to know about a packet.
    public struct Entry: Sendable {
        public let packet: Packet
        public let isVideo: Bool
        public let isKeyFrame: Bool
        public let isCodecConfig: Bool

        /// Creates an entry.
        public init(packet: Packet, isVideo: Bool, isKeyFrame: Bool, isCodecConfig: Bool) {
            self.packet = packet
            self.isVideo = isVideo
            self.isKeyFrame = isKeyFrame
            self.isCodecConfig = isCodecConfig
        }
    }

    private var entries: [Entry] = []
    private let maxVideoFrames: Int

    /// Video frames dropped so far; a rising count means the bitrate should come down.
    public private(set) var droppedVideoFrames = 0

    /// Creates a queue.
    /// - Parameter maxVideoFrames: Ordinary video frames kept before the oldest is dropped.
    public init(maxVideoFrames: Int = 8) {
        self.maxVideoFrames = maxVideoFrames
    }

    /// Packets waiting.
    public var count: Int {
        entries.count
    }

    /// Adds a packet, dropping video that can no longer be useful.
    /// - Parameter entry: New packet.
    public mutating func push(_ entry: Entry) {
        if entry.isVideo && entry.isKeyFrame && !entry.isCodecConfig {
            // A key frame replaces every video frame still waiting (its config is already queued).
            let before = entries.count
            entries.removeAll { $0.isVideo && !$0.isCodecConfig }
            droppedVideoFrames += before - entries.count
        }
        entries.append(entry)
        let ordinary = entries.filter { $0.isVideo && !$0.isKeyFrame && !$0.isCodecConfig }.count
        if ordinary > maxVideoFrames, let index = entries.firstIndex(where: { $0.isVideo && !$0.isKeyFrame && !$0.isCodecConfig }) {
            entries.remove(at: index)
            droppedVideoFrames += 1
        }
    }

    /// Takes the oldest packet.
    public mutating func pop() -> Packet? {
        entries.isEmpty ? nil : entries.removeFirst().packet
    }
}
