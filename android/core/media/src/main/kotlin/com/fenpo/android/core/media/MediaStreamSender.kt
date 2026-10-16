package com.fenpo.android.core.media

import android.os.SystemClock
import android.util.Log
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.transport.MediaFraming
import com.fenpo.android.core.transport.tls.DataConnection
import com.fenpo.protocol.v1.MediaStreamOpen
import com.fenpo.protocol.v1.StreamPacket
import com.fenpo.protocol.v1.StreamTrack
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okio.blackholeSink
import java.io.IOException
import java.util.concurrent.atomic.AtomicInteger

/**
 * One media connection to the PC (protocol/README "Media connections"). Encoders hand packets to
 * [offer] from their own threads; a writer sends them in order. When the connection falls behind,
 * video is dropped up to the next key frame instead of piling up, and [onCongestion] lets the
 * owner lower the bitrate and ask for that key frame.
 */
class MediaStreamSender(
    private val messenger: PcMessenger,
    private val open: MediaStreamOpen,
    private val onCongestion: () -> Unit,
) {
    private val queue = Channel<StreamPacket>(QUEUE)

    @Volatile private var waitingForKeyFrame = false

    @Volatile private var connection: DataConnection? = null

    // Counters for the 5-second log line: frames from the encoder, frames dropped, bytes written.
    private val framesIn = AtomicInteger()
    private val framesDropped = AtomicInteger()

    /** Called from encoder threads. */
    fun offer(packet: StreamPacket) {
        val video = packet.track == StreamTrack.STREAM_TRACK_VIDEO
        if (video && !packet.codec_config) framesIn.incrementAndGet()
        if (video && waitingForKeyFrame) {
            if (!packet.codec_config && !packet.key_frame) {
                framesDropped.incrementAndGet()
                return
            }
            waitingForKeyFrame = false
        }
        if (queue.trySend(packet).isFailure && video) {
            framesDropped.incrementAndGet()
            // Behind: a missing P-frame breaks every frame up to the next key frame, so skip to it.
            waitingForKeyFrame = true
            onCongestion()
        }
    }

    /**
     * Opens the connection and writes until [close] or a network failure.
     *
     * @return `false` when the connection could not be opened (no Wi-Fi) or broke.
     */
    suspend fun run(): Boolean =
        withContext(Dispatchers.IO) {
            val data = messenger.openDataConnection(0) ?: return@withContext false
            connection = data
            // Read whatever the PC sends (TLS session tickets) so closing does not reset the connection.
            val drain = CoroutineScope(Dispatchers.IO).launch { runCatching { data.source.readAll(blackholeSink()) } }
            try {
                MediaFraming.writeOpen(data.sink, open)
                var bytes = 0L
                var oldestMs = 0L
                var since = SystemClock.elapsedRealtime()
                for (packet in queue) {
                    MediaFraming.writePacket(data.sink, packet)
                    bytes += packet.data_.size
                    if (queue.isEmpty) data.sink.flush()
                    if (packet.track == StreamTrack.STREAM_TRACK_VIDEO && !packet.codec_config) {
                        oldestMs = maxOf(oldestMs, ageMs(packet.pts_us))
                    }
                    val now = SystemClock.elapsedRealtime()
                    if (now - since >= STATS_MS) {
                        val seconds = (now - since) / MILLIS
                        val fps = framesIn.getAndSet(0) / seconds
                        val dropped = framesDropped.getAndSet(0)
                        val kbps = bytes * BITS / seconds / KILO
                        Log.i(
                            TAG,
                            "Stream ${open.stream_id.take(ID_CHARS)}: $fps fps in, $dropped dropped, $kbps kbps, " +
                                "queue ${queueDepth()}, oldest frame $oldestMs ms after capture",
                        )
                        bytes = 0
                        oldestMs = 0
                        since = now
                    }
                    if (!isActive) break
                }
                true
            } catch (e: IOException) {
                Log.i(TAG, "Media connection ${open.stream_id} ended: ${e.message}")
                false
            } finally {
                drain.cancel()
                data.close()
            }
        }

    /**
     * How long ago a frame was captured. Camera timestamps use either the boot clock or the
     * monotonic clock depending on the sensor; the smaller non-negative age is the right one.
     */
    private fun ageMs(ptsUs: Long): Long {
        val boot = SystemClock.elapsedRealtimeNanos() / NANOS_PER_MICRO - ptsUs
        val monotonic = System.nanoTime() / NANOS_PER_MICRO - ptsUs
        return listOf(boot, monotonic).filter { it >= 0 }.minOrNull()?.div(MICROS_PER_MILLI) ?: 0
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    private fun queueDepth(): String = if (queue.isEmpty) "empty" else "busy"

    /** Ends the stream after what is queued. */
    fun close() {
        queue.close()
    }

    private companion object {
        const val TAG = "FenpoMedia"

        // About a second of video at 30 fps plus audio; more only adds latency.
        const val QUEUE = 48
        const val STATS_MS = 5_000L
        const val MILLIS = 1000.0
        const val BITS = 8
        const val KILO = 1000
        const val ID_CHARS = 8
        const val NANOS_PER_MICRO = 1000L
        const val MICROS_PER_MILLI = 1000L
    }
}

/**
 * Bitrate under congestion (protocol/README): down by a quarter on every drop, never below the
 * floor; back up by a tenth after 10 quiet seconds, never above the target.
 */
class BitrateController(
    private val targetKbps: Int,
    private val floorKbps: Int,
) {
    var currentKbps: Int = targetKbps
        private set
    private var lastDropMs = Long.MIN_VALUE / 2
    private var lastRaiseMs = Long.MIN_VALUE / 2

    fun onDrop(nowMs: Long): Int {
        lastDropMs = nowMs
        currentKbps = maxOf(floorKbps, currentKbps * 3 / 4)
        return currentKbps
    }

    fun onTick(nowMs: Long): Int {
        if (nowMs - lastDropMs >= QUIET_MS && nowMs - lastRaiseMs >= QUIET_MS && currentKbps < targetKbps) {
            lastRaiseMs = nowMs
            currentKbps = minOf(targetKbps, currentKbps + maxOf(1, currentKbps / 10))
        }
        return currentKbps
    }

    private companion object {
        const val QUIET_MS = 10_000L
    }
}
