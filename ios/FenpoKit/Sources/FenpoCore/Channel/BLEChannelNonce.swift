/// AES-GCM nonces and replay protection on the Fenpo BLE channel.
///
/// Follows the S5 proposal for DECISIONS #23 until the protocol commit lands:
/// nonce = `[1 byte sender][3 zero bytes][8 byte counter, big-endian]`, associated data
/// `"fenpo-ble-v1"`, the whole Envelope is sealed before chunking, and a receiver rejects any
/// counter that is not larger than the last one it accepted. The sealed message is
/// `nonce || ciphertext || tag`.
public enum BLEChannelNonce {
    /// Who sealed a message.
    public enum Sender: UInt8, Sendable {
        case phone = 0x01
        case pc = 0x02
    }

    /// Nonce size in bytes.
    public static let length = 12

    /// Associated data bound to every sealed message.
    public static let associatedData: [UInt8] = Array("fenpo-ble-v1".utf8)

    /// Why a received nonce is not accepted.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The message is too short to hold a nonce.
        case truncated

        /// The nonce does not have the expected layout or sender.
        case unexpectedSender(UInt8)

        /// The counter is not larger than the last accepted one (replay or reorder).
        case replayed(counter: UInt64, lastAccepted: UInt64)
    }

    /// Builds a nonce.
    /// - Parameters:
    ///   - sender: Who seals the message.
    ///   - counter: Sender's counter value.
    public static func make(sender: Sender, counter: UInt64) -> [UInt8] {
        var nonce: [UInt8] = [sender.rawValue, 0, 0, 0]
        for shift in stride(from: 56, through: 0, by: -8) {
            nonce.append(UInt8(truncatingIfNeeded: counter >> UInt64(shift)))
        }
        return nonce
    }

    /// Reads the sender and counter of a nonce.
    /// - Parameter nonce: The first 12 bytes of a sealed message.
    public static func parse(_ nonce: some Collection<UInt8>) throws(Error) -> (sender: UInt8, counter: UInt64) {
        let bytes = Array(nonce.prefix(length))
        guard bytes.count == length else {
            throw .truncated
        }
        guard bytes[1] == 0, bytes[2] == 0, bytes[3] == 0 else {
            throw .unexpectedSender(bytes[0])
        }
        let counter = bytes[4...].reduce(UInt64(0)) { $0 << 8 | UInt64($1) }
        return (bytes[0], counter)
    }
}

/// Counter state of one side of the BLE channel. Persist it: counters must never repeat for a key.
public struct BLEChannelCounters: Equatable, Codable, Sendable {
    /// Counter of the last message this side sealed.
    public private(set) var lastSent: UInt64

    /// Counter of the last message accepted from the other side.
    public private(set) var lastReceived: UInt64

    /// Creates counters, by default for a fresh key.
    /// - Parameters:
    ///   - lastSent: Counter of the last sealed message.
    ///   - lastReceived: Counter of the last accepted message.
    public init(lastSent: UInt64 = 0, lastReceived: UInt64 = 0) {
        self.lastSent = lastSent
        self.lastReceived = lastReceived
    }

    /// Advances the send counter and returns the nonce for the next message.
    /// - Parameter sender: This side.
    public mutating func nextNonce(from sender: BLEChannelNonce.Sender) -> [UInt8] {
        lastSent += 1
        return BLEChannelNonce.make(sender: sender, counter: lastSent)
    }

    /// Checks a received nonce and remembers its counter.
    /// - Parameters:
    ///   - nonce: Nonce of the received message.
    ///   - peer: The side that must have sent it.
    public mutating func accept(_ nonce: some Collection<UInt8>, from peer: BLEChannelNonce.Sender) throws(BLEChannelNonce.Error) {
        let (sender, counter) = try BLEChannelNonce.parse(nonce)
        guard sender == peer.rawValue else {
            throw .unexpectedSender(sender)
        }
        guard counter > lastReceived else {
            throw .replayed(counter: counter, lastAccepted: lastReceived)
        }
        lastReceived = counter
    }
}

/// Seals and opens messages on the Fenpo BLE channel. The Apple implementation uses AES-GCM.
public protocol ChannelCipher: Sendable {
    /// Encrypts and authenticates a serialized Envelope.
    /// - Parameter plaintext: Serialized Envelope.
    mutating func seal(_ plaintext: [UInt8]) throws -> [UInt8]

    /// Checks and decrypts a sealed message.
    /// - Parameter message: Sealed message as received.
    mutating func open(_ message: [UInt8]) throws -> [UInt8]

    /// Counter state to persist after each seal and open.
    var counters: BLEChannelCounters { get }
}
