/// Chunking on the Fenpo BLE channel (docs/ARCHITECTURE.md): each write or notification is
/// `[1 byte flags][2 byte sequence, big-endian][payload]`.
///
/// Flags: bit 0 first chunk, bit 1 last chunk. The sequence numbers the chunks of one message
/// from 0. Messages are sealed before chunking and must stay under `maxMessageLength`; larger
/// content goes over Wi-Fi. Bit values and sequence scope follow the S5 proposal and are confirmed
/// by the protocol commit that closes DECISIONS #23.
public enum BLEChunking {
    /// Header size in bytes.
    public static let headerLength = 3

    /// Largest sealed message the BLE channel carries.
    public static let maxMessageLength = 4096

    /// Chunk header flags.
    public struct Flags: OptionSet, Sendable {
        public let rawValue: UInt8

        public init(rawValue: UInt8) {
            self.rawValue = rawValue
        }

        /// First chunk of a message.
        public static let first = Flags(rawValue: 1 << 0)

        /// Last chunk of a message.
        public static let last = Flags(rawValue: 1 << 1)
    }

    /// Why a message could not be chunked or reassembled.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The message is larger than `maxMessageLength`.
        case messageTooLarge(Int)

        /// The write size leaves no room for payload.
        case chunkTooSmall(Int)

        /// A chunk is shorter than its header.
        case truncatedChunk

        /// A chunk arrived without a first chunk before it.
        case missingFirstChunk

        /// A chunk arrived out of order; the partial message was dropped.
        case unexpectedSequence(expected: UInt16, received: UInt16)
    }

    /// Splits a sealed message into chunks that fit one write.
    /// - Parameters:
    ///   - message: Sealed message.
    ///   - maximumChunkLength: Largest write the link accepts (for example `maximumWriteValueLength`).
    public static func chunks(of message: [UInt8], maximumChunkLength: Int) throws(Error) -> [[UInt8]] {
        guard message.count <= maxMessageLength else {
            throw .messageTooLarge(message.count)
        }
        let payloadLength = maximumChunkLength - headerLength
        guard payloadLength > 0 else {
            throw .chunkTooSmall(maximumChunkLength)
        }

        var chunks: [[UInt8]] = []
        var offset = 0
        var sequence: UInt16 = 0
        repeat {
            let end = min(offset + payloadLength, message.count)
            var flags: Flags = []
            if offset == 0 {
                flags.insert(.first)
            }
            if end == message.count {
                flags.insert(.last)
            }
            var chunk: [UInt8] = [flags.rawValue, UInt8(truncatingIfNeeded: sequence >> 8), UInt8(truncatingIfNeeded: sequence)]
            chunk.append(contentsOf: message[offset..<end])
            chunks.append(chunk)
            offset = end
            sequence &+= 1
        } while offset < message.count
        return chunks
    }
}

/// Puts chunks back together into sealed messages.
public struct BLEReassembler: Sendable {
    private var buffer: [UInt8] = []
    private var nextSequence: UInt16?

    /// Creates an empty reassembler.
    public init() {
    }

    /// Adds a received chunk; returns the message when this chunk completes one.
    /// - Parameter chunk: Value of one write or notification.
    public mutating func append(_ chunk: [UInt8]) throws(BLEChunking.Error) -> [UInt8]? {
        guard chunk.count >= BLEChunking.headerLength else {
            reset()
            throw .truncatedChunk
        }
        let flags = BLEChunking.Flags(rawValue: chunk[0])
        let sequence = UInt16(chunk[1]) << 8 | UInt16(chunk[2])
        let payload = chunk[BLEChunking.headerLength...]

        if flags.contains(.first) {
            buffer.removeAll(keepingCapacity: true)
            nextSequence = 0
        }
        guard let expected = nextSequence else {
            throw .missingFirstChunk
        }
        guard sequence == expected else {
            reset()
            throw .unexpectedSequence(expected: expected, received: sequence)
        }
        guard buffer.count + payload.count <= BLEChunking.maxMessageLength else {
            let size = buffer.count + payload.count
            reset()
            throw .messageTooLarge(size)
        }

        buffer.append(contentsOf: payload)
        nextSequence = expected &+ 1

        guard flags.contains(.last) else {
            return nil
        }
        let message = buffer
        reset()
        return message
    }

    private mutating func reset() {
        buffer.removeAll(keepingCapacity: true)
        nextSequence = nil
    }
}
