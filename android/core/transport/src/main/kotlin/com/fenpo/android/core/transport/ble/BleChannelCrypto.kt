package com.fenpo.android.core.transport.ble

import java.nio.ByteBuffer
import java.security.GeneralSecurityException
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * AES-256-GCM for the Fenpo BLE channel (protocol/README.md, "Sealing"):
 *
 * - Key: `PairingResult.ble_channel_key` (32 bytes). The BLE bond alone is not trusted.
 * - Whole Envelope sealed first, split into chunks afterwards ([BleFragmenter]).
 * - Nonce, 12 bytes: `[1 byte sender: 0x01 phone, 0x02 PC][3 zero bytes][8 byte counter, big-endian]`.
 * - Associated data: `"fenpo-ble-v1"`.
 * - Sealed message: `nonce || ciphertext || tag`, CryptoKit's `combined` layout; the receiver
 *   reads the counter from the nonce. At most [MAX_SEALED_BYTES].
 * - Every sender keeps its counter persistent and increasing; the receiver rejects a counter that
 *   is not larger than the last one it accepted (replay protection).
 *
 * Not thread safe; the connection serializes sends and receives.
 *
 * @param nextSendCounter Counter for the next message this side sends; persist [nextSendCounter]
 *   after each [seal] so it never repeats, even after a restart.
 * @param lastReceivedCounter Highest counter accepted from the other side so far.
 */
public class BleChannelCrypto(
    key: ByteArray,
    private val localSender: Sender,
    nextSendCounter: Long = 1,
    lastReceivedCounter: Long = 0,
) {
    /** Who sends a message; part of the nonce so both directions never share one. */
    public enum class Sender(
        internal val id: Byte,
    ) {
        PHONE(0x01),
        PC(0x02),
    }

    init {
        require(key.size == KEY_BYTES) { "BLE channel key must be $KEY_BYTES bytes" }
    }

    private val key = SecretKeySpec(key.copyOf(), "AES")
    private val remoteSender = if (localSender == Sender.PHONE) Sender.PC else Sender.PHONE

    /** Counter the next [seal] will use. */
    public var nextSendCounter: Long = nextSendCounter
        private set

    /** Highest counter accepted by [open]. */
    public var lastReceivedCounter: Long = lastReceivedCounter
        private set

    /** Seals one serialized Envelope. */
    public fun seal(plaintext: ByteArray): ByteArray {
        require(plaintext.size <= MAX_PLAINTEXT_BYTES) { "Envelope of ${plaintext.size} bytes is too big for BLE" }
        val nonce = nonce(localSender, nextSendCounter++)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(TAG_BITS, nonce))
        cipher.updateAAD(AAD)
        return nonce + cipher.doFinal(plaintext)
    }

    /**
     * Opens one message from the other side.
     *
     * @throws GeneralSecurityException if it was changed, comes from the wrong sender, uses the
     *   wrong key or replays an old counter.
     */
    public fun open(message: ByteArray): ByteArray {
        if (message.size < OVERHEAD_BYTES || message.size > MAX_SEALED_BYTES) {
            throw GeneralSecurityException("Sealed message of ${message.size} bytes")
        }
        val header = ByteBuffer.wrap(message, 0, NONCE_BYTES)
        if (header.get() != remoteSender.id || header.get() != ZERO || header.get() != ZERO || header.get() != ZERO) {
            throw GeneralSecurityException("Nonce is not from the other side")
        }
        val counter = header.long
        if (counter <= lastReceivedCounter) throw GeneralSecurityException("Replayed counter $counter")
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(TAG_BITS, message, 0, NONCE_BYTES))
        cipher.updateAAD(AAD)
        val plaintext = cipher.doFinal(message, NONCE_BYTES, message.size - NONCE_BYTES)
        lastReceivedCounter = counter
        return plaintext
    }

    private fun nonce(
        sender: Sender,
        counter: Long,
    ): ByteArray =
        ByteBuffer
            .allocate(NONCE_BYTES)
            .put(sender.id)
            .put(ByteArray(3))
            .putLong(counter)
            .array()

    public companion object {
        /** Size of `ble_channel_key`. */
        public const val KEY_BYTES: Int = 32

        /** Largest sealed message on BLE; anything bigger goes over Wi‑Fi. */
        public const val MAX_SEALED_BYTES: Int = 4096

        /** Bytes sealing adds to the plaintext: the nonce in front and the GCM tag. */
        public const val OVERHEAD_BYTES: Int = 12 + 16

        /** Largest Envelope that still fits in [MAX_SEALED_BYTES]. */
        public const val MAX_PLAINTEXT_BYTES: Int = MAX_SEALED_BYTES - OVERHEAD_BYTES

        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val TAG_BITS = 128
        private const val NONCE_BYTES = 12
        private const val ZERO: Byte = 0
        private val AAD = "fenpo-ble-v1".toByteArray(Charsets.US_ASCII)
    }
}
