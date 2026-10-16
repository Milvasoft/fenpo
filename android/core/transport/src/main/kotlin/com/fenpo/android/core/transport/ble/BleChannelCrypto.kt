package com.fenpo.android.core.transport.ble

import java.nio.ByteBuffer
import java.security.GeneralSecurityException
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * AES-256-GCM for the Fenpo BLE channel, following the S5 protocol proposal (DECISIONS #23):
 *
 * - Key: `PairingResult.ble_channel_key` (32 bytes). The BLE bond alone is not trusted.
 * - Whole Envelope encrypted first, fragmented afterwards ([BleFragmenter]).
 * - Nonce, 12 bytes: `[1 byte sender: 0x01 phone, 0x02 PC][3 zero bytes][8 byte counter, big-endian]`.
 * - Associated data: `"fenpo-ble-v1"`.
 * - Every sender keeps its counter persistent and increasing; the receiver rejects a counter that
 *   is not larger than the last one it accepted (replay protection).
 *
 * The proposal does not say how the receiver learns the counter. Here the sealed message is
 * `[8 byte counter][ciphertext and tag]`; to be confirmed in the protocol commit.
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

    /** Encrypts one serialized Envelope. */
    public fun seal(plaintext: ByteArray): ByteArray {
        val counter = nextSendCounter++
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(TAG_BITS, nonce(localSender, counter)))
        cipher.updateAAD(AAD)
        val sealed = cipher.doFinal(plaintext)
        return ByteBuffer
            .allocate(COUNTER_BYTES + sealed.size)
            .putLong(counter)
            .put(sealed)
            .array()
    }

    /**
     * Decrypts one message from the other side.
     *
     * @throws GeneralSecurityException if it was changed, uses the wrong key or replays an old counter.
     */
    public fun open(message: ByteArray): ByteArray {
        if (message.size < COUNTER_BYTES + TAG_BITS / 8) throw GeneralSecurityException("Message too short")
        val counter = ByteBuffer.wrap(message, 0, COUNTER_BYTES).long
        if (counter <= lastReceivedCounter) throw GeneralSecurityException("Replayed counter $counter")
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(TAG_BITS, nonce(remoteSender, counter)))
        cipher.updateAAD(AAD)
        val plaintext = cipher.doFinal(message, COUNTER_BYTES, message.size - COUNTER_BYTES)
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

        /** Bytes a sealed message adds to its plaintext: counter and GCM tag. */
        public const val OVERHEAD_BYTES: Int = 8 + 16

        private const val TRANSFORMATION = "AES/GCM/NoPadding"
        private const val TAG_BITS = 128
        private const val NONCE_BYTES = 12
        private const val COUNTER_BYTES = 8
        private val AAD = "fenpo-ble-v1".toByteArray(Charsets.US_ASCII)
    }
}
