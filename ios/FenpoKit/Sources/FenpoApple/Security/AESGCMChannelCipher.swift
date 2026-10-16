import CryptoKit
import FenpoCore
import Foundation

/// AES-256-GCM on the Fenpo BLE channel, with the nonce layout and replay guard of `BLEChannelNonce`.
///
/// Sealed message: `nonce (12) || ciphertext || tag (16)`. Provisional until the protocol commit
/// that closes DECISIONS #23; the BLE client only depends on `ChannelCipher`.
public struct AESGCMChannelCipher: ChannelCipher {
    /// Why a key or message is rejected.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The key is not 32 bytes.
        case invalidKeyLength(Int)
    }

    private let key: SymmetricKey
    private let sender: BLEChannelNonce.Sender
    private let peer: BLEChannelNonce.Sender

    public private(set) var counters: BLEChannelCounters

    /// Creates a cipher.
    /// - Parameters:
    ///   - key: `PairingResult.ble_channel_key`, 32 bytes.
    ///   - sender: This side.
    ///   - counters: Persisted counters for this key.
    public init(key: [UInt8], sender: BLEChannelNonce.Sender = .phone, counters: BLEChannelCounters = BLEChannelCounters()) throws {
        guard key.count == 32 else {
            throw Error.invalidKeyLength(key.count)
        }
        self.key = SymmetricKey(data: key)
        self.sender = sender
        self.peer = sender == .phone ? .pc : .phone
        self.counters = counters
    }

    public mutating func seal(_ plaintext: [UInt8]) throws -> [UInt8] {
        let nonce = try AES.GCM.Nonce(data: counters.nextNonce(from: sender))
        let box = try AES.GCM.seal(plaintext, using: key, nonce: nonce, authenticating: BLEChannelNonce.associatedData)
        guard let combined = box.combined else {
            throw CryptoKitError.incorrectParameterSize
        }
        return Array(combined)
    }

    public mutating func open(_ message: [UInt8]) throws -> [UInt8] {
        let box = try AES.GCM.SealedBox(combined: message)
        let plaintext = try AES.GCM.open(box, using: key, authenticating: BLEChannelNonce.associatedData)
        // Only authentic messages move the replay window.
        try counters.accept(message.prefix(BLEChannelNonce.length), from: peer)
        return Array(plaintext)
    }
}
