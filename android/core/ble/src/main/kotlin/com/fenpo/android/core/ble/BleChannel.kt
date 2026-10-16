package com.fenpo.android.core.ble

import android.util.Log
import com.fenpo.android.core.protocol.ProtocolVersion
import com.fenpo.android.core.transport.ble.BleChannelCrypto
import com.fenpo.android.core.transport.ble.BleFragmenter
import com.fenpo.android.core.transport.ble.BleReassembler
import com.fenpo.protocol.v1.Envelope
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.mapNotNull
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.IOException
import java.util.concurrent.atomic.AtomicLong

/** Where the channel keeps its AES-GCM counters between connections and restarts. */
interface BleCounterStore {
    /** Called before a message with [nextSendCounter] − 1 leaves the phone, so a counter is never reused. */
    suspend fun saveSendCounter(nextSendCounter: Long)

    /** Called after a message from the PC was accepted, so a replay is refused after a restart too. */
    suspend fun saveReceivedCounter(lastReceivedCounter: Long)
}

/** An Envelope is too big for BLE; send it over Wi‑Fi or announce Wi‑Fi on BLE (docs/ARCHITECTURE.md). */
class TooLargeForBleException(
    size: Int,
) : IOException("Envelope of $size bytes exceeds ${BleChannel.MAX_ENVELOPE_BYTES} bytes")

/**
 * The Fenpo BLE channel (③) on top of a [GattConnection]: Envelope → AES-GCM ([BleChannelCrypto])
 * → fragments ([BleFragmenter]) → `rx` writes, and `tx` notifications back the other way.
 */
class BleChannel(
    private val gatt: GattConnection,
    private val crypto: BleChannelCrypto,
    private val counters: BleCounterStore,
) {
    private val sendLock = Mutex()
    private val nextId = AtomicLong(1)
    private val reassembler = BleReassembler(MAX_ENVELOPE_BYTES + BleChannelCrypto.OVERHEAD_BYTES)
    private var mtu = 23

    /** Connects; see [GattConnection.connect] for [autoConnect]. */
    suspend fun open(autoConnect: Boolean) {
        mtu = gatt.connect(autoConnect)
        Log.i(TAG, "BLE channel ready, MTU $mtu")
    }

    /** Envelopes from the PC that decrypted and parsed; anything else is logged and dropped. */
    val incoming: Flow<Envelope> =
        gatt.notifications.mapNotNull { fragment ->
            val sealed = reassembler.accept(fragment) ?: return@mapNotNull null
            try {
                val plaintext = crypto.open(sealed)
                counters.saveReceivedCounter(crypto.lastReceivedCounter)
                Envelope.ADAPTER.decode(plaintext)
            } catch (e: Exception) {
                Log.w(TAG, "Dropped a BLE message: ${e.message}")
                null
            }
        }

    /**
     * Sends [body] and returns the envelope id.
     *
     * @throws TooLargeForBleException above [MAX_ENVELOPE_BYTES].
     */
    suspend fun send(
        body: Envelope.Body,
        replyTo: Long = 0,
    ): Long =
        sendLock.withLock {
            val id = nextId.getAndIncrement()
            val plaintext =
                Envelope.ADAPTER.encode(
                    Envelope(version = ProtocolVersion.V1, id = id, reply_to = replyTo, body = body),
                )
            if (plaintext.size > MAX_ENVELOPE_BYTES) throw TooLargeForBleException(plaintext.size)
            counters.saveSendCounter(crypto.nextSendCounter + 1)
            val sealed = crypto.seal(plaintext)
            BleFragmenter.split(sealed, mtu).forEach { gatt.write(it) }
            id
        }

    /** Closes the link. */
    fun close() = gatt.close()

    companion object {
        /** Largest Envelope sent over BLE (docs/ARCHITECTURE.md: "Sadece 4 KB'tan küçük mesajlar"). */
        const val MAX_ENVELOPE_BYTES = 4096
        private const val TAG = "FenpoBle"
    }
}
