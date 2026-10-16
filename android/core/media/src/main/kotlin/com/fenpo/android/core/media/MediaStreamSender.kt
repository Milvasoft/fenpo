package com.fenpo.android.core.media

import android.util.Log
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.transport.MediaFraming
import com.fenpo.android.core.transport.tls.DataConnection
import com.fenpo.protocol.v1.MediaStreamOpen
import com.fenpo.protocol.v1.StreamPacket
import com.fenpo.protocol.v1.StreamTrack
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okio.blackholeSink
import java.io.IOException

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

    /** Called from encoder threads. */
    fun offer(packet: StreamPacket) {
        val video = packet.track == StreamTrack.STREAM_TRACK_VIDEO
        if (video && waitingForKeyFrame) {
            if (!packet.codec_config && !packet.key_frame) return
            waitingForKeyFrame = false
        }
        if (queue.trySend(packet).isFailure && video) {
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
                for (packet in queue) {
                    MediaFraming.writePacket(data.sink, packet)
                    if (queue.isEmpty) data.sink.flush()
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

    /** Ends the stream after what is queued. */
    fun close() {
        queue.close()
    }

    private companion object {
        const val TAG = "FenpoMedia"

        // About a second of video at 30 fps plus audio; more only adds latency.
        const val QUEUE = 48
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
