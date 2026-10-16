/// Framing of the TLS control connection: `[uint32 big-endian length][Envelope bytes]`.
public enum EnvelopeFraming {
    /// Largest Envelope accepted or sent. Matches the Windows side.
    public static let maxFrameLength = 1 << 20

    /// Length prefix size in bytes.
    public static let headerLength = 4

    /// Why a frame could not be written or read.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The frame is longer than `maxFrameLength`.
        case frameTooLarge(Int)
    }

    /// Prefixes a serialized Envelope with its length.
    /// - Parameter payload: Serialized Envelope.
    public static func frame(_ payload: [UInt8]) throws(Error) -> [UInt8] {
        guard payload.count <= maxFrameLength else {
            throw .frameTooLarge(payload.count)
        }
        let length = UInt32(payload.count)
        var frame: [UInt8] = [
            UInt8(truncatingIfNeeded: length >> 24),
            UInt8(truncatingIfNeeded: length >> 16),
            UInt8(truncatingIfNeeded: length >> 8),
            UInt8(truncatingIfNeeded: length),
        ]
        frame.append(contentsOf: payload)
        return frame
    }
}

/// Splits a byte stream into Envelope payloads. Bytes may arrive in any chunking.
public struct EnvelopeFrameDecoder: Sendable {
    private var buffer: [UInt8] = []

    /// Creates an empty decoder.
    public init() {
    }

    /// Bytes received but not yet part of a complete frame.
    public var bufferedByteCount: Int {
        buffer.count
    }

    /// Adds received bytes and returns every frame they complete, in order.
    /// - Parameter bytes: Newly received bytes.
    public mutating func append(_ bytes: some Sequence<UInt8>) throws(EnvelopeFraming.Error) -> [[UInt8]] {
        buffer.append(contentsOf: bytes)
        var frames: [[UInt8]] = []
        var offset = 0
        while buffer.count - offset >= EnvelopeFraming.headerLength {
            let length = Int(buffer[offset]) << 24
                | Int(buffer[offset + 1]) << 16
                | Int(buffer[offset + 2]) << 8
                | Int(buffer[offset + 3])
            guard length <= EnvelopeFraming.maxFrameLength else {
                buffer.removeAll()
                throw .frameTooLarge(length)
            }
            let start = offset + EnvelopeFraming.headerLength
            guard buffer.count - start >= length else {
                break
            }
            frames.append(Array(buffer[start..<(start + length)]))
            offset = start + length
        }
        buffer.removeFirst(offset)
        return frames
    }
}
