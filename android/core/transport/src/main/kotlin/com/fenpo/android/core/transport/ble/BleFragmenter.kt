package com.fenpo.android.core.transport.ble

import java.io.ByteArrayOutputStream

/**
 * Splits a sealed message into GATT writes and joins notifications back (docs/ARCHITECTURE.md,
 * "Çerçeveleme"): every fragment is `[1 byte flags][2 byte sequence, big-endian][chunk]`, where
 * the flags mark the first and last fragment and the sequence counts fragments within a message.
 */
public object BleFragmenter {
    /** Flags byte: this fragment starts a message. */
    public const val FIRST: Int = 0x01

    /** Flags byte: this fragment ends a message. */
    public const val LAST: Int = 0x02

    /** Header size of every fragment. */
    public const val HEADER_BYTES: Int = 3

    /** ATT header taken from the MTU: payload of a write or notification is MTU − 3. */
    public const val ATT_OVERHEAD: Int = 3

    /**
     * Fragments [message] for a link with the given ATT [mtu].
     *
     * @throws IllegalArgumentException if the MTU leaves no room for data or the message needs
     *   more fragments than the sequence can count.
     */
    public fun split(
        message: ByteArray,
        mtu: Int,
    ): List<ByteArray> {
        val chunk = mtu - ATT_OVERHEAD - HEADER_BYTES
        require(chunk > 0) { "MTU $mtu leaves no room for data" }
        val count = maxOf(1, (message.size + chunk - 1) / chunk)
        require(count <= MAX_FRAGMENTS) { "Message of ${message.size} bytes needs $count fragments" }
        return List(count) { index ->
            val start = index * chunk
            val end = minOf(message.size, start + chunk)
            var flags = 0
            if (index == 0) flags = flags or FIRST
            if (index == count - 1) flags = flags or LAST
            ByteArray(HEADER_BYTES + end - start).also {
                it[0] = flags.toByte()
                it[1] = (index ushr 8).toByte()
                it[2] = index.toByte()
                message.copyInto(it, HEADER_BYTES, start, end)
            }
        }
    }

    private const val MAX_FRAGMENTS = 0xFFFF + 1
}

/**
 * Joins fragments from one sender back into messages. A gap, a repeat or a fragment without a
 * start drops the message being built; BLE writes without response can be lost, and a partial
 * message must never reach the decryptor as if it were whole.
 *
 * @param maxMessageBytes Upper bound for one sealed message; longer ones are dropped.
 */
public class BleReassembler(
    private val maxMessageBytes: Int,
) {
    private var buffer: ByteArrayOutputStream? = null
    private var expected = 0

    // After a drop, the rest of that message is skipped silently until the next first fragment.
    private var skipping = false

    /** Messages dropped so far because of gaps or oversize; for logs. */
    public var dropped: Int = 0
        private set

    /** Adds one fragment and returns the completed message, if this fragment finished one. */
    public fun accept(fragment: ByteArray): ByteArray? {
        if (fragment.size < BleFragmenter.HEADER_BYTES) return drop()
        val flags = fragment[0].toInt() and 0xFF
        val sequence = ((fragment[1].toInt() and 0xFF) shl 8) or (fragment[2].toInt() and 0xFF)

        if (flags and BleFragmenter.FIRST != 0) {
            if (buffer != null) dropped++
            skipping = false
            if (sequence != 0) return drop()
            buffer = ByteArrayOutputStream()
            expected = 0
        }
        val current = buffer ?: return drop()
        if (sequence != expected) return drop()

        current.write(fragment, BleFragmenter.HEADER_BYTES, fragment.size - BleFragmenter.HEADER_BYTES)
        if (current.size() > maxMessageBytes) return drop()
        expected = (expected + 1) and 0xFFFF

        if (flags and BleFragmenter.LAST == 0) return null
        buffer = null
        return current.toByteArray()
    }

    private fun drop(): ByteArray? {
        buffer = null
        if (!skipping) dropped++
        skipping = true
        return null
    }
}
