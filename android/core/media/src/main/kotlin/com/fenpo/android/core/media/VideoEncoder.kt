package com.fenpo.android.core.media

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import android.view.Surface
import com.fenpo.protocol.v1.StreamPacket
import com.fenpo.protocol.v1.StreamTrack
import com.fenpo.protocol.v1.VideoCodec
import okio.ByteString
import okio.ByteString.Companion.toByteString

/**
 * Hardware video encoder fed through its input [surface] (camera or virtual display), tuned for
 * live viewing on the PC (protocol/README "Media connections"): Annex-B, no B-frames, constant
 * bitrate, a key frame about every 2 s and on request, codec config before every key frame.
 *
 * @param onPacket Called on the encoder thread for every encoded access unit.
 */
class VideoEncoder(
    val codec: VideoCodec,
    val width: Int,
    val height: Int,
    val fps: Int,
    bitrateKbps: Int,
    private val onPacket: (StreamPacket) -> Unit,
) {
    private val thread = HandlerThread("FenpoVideoEncoder").apply { start() }
    private val encoder = MediaCodec.createEncoderByType(mime(codec))
    private var config: ByteString? = null

    /** Where the camera or the virtual display draws. Valid until [stop]. */
    val surface: Surface

    @Volatile var bitrateKbps: Int = bitrateKbps
        private set

    init {
        val format =
            MediaFormat.createVideoFormat(mime(codec), width, height).apply {
                setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
                setInteger(MediaFormat.KEY_BIT_RATE, bitrateKbps * KBPS)
                setInteger(MediaFormat.KEY_FRAME_RATE, fps)
                setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, KEY_FRAME_SECONDS)
                setInteger(MediaFormat.KEY_MAX_B_FRAMES, 0)
                setInteger(MediaFormat.KEY_PRIORITY, 0)
                setInteger(MediaFormat.KEY_LATENCY, 1)
                setInteger(MediaFormat.KEY_PREPEND_HEADER_TO_SYNC_FRAMES, 1)
                // A still screen produces no frames; repeat the last one so the PC keeps a picture.
                setLong(MediaFormat.KEY_REPEAT_PREVIOUS_FRAME_AFTER, REPEAT_AFTER_US)
                if (codec == VideoCodec.VIDEO_CODEC_H264) {
                    setInteger(MediaFormat.KEY_PROFILE, MediaCodecInfo.CodecProfileLevel.AVCProfileMain)
                }
                val modes = encoder.codecInfo.getCapabilitiesForType(mime(codec)).encoderCapabilities
                if (modes?.isBitrateModeSupported(MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_CBR) == true) {
                    setInteger(MediaFormat.KEY_BITRATE_MODE, MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_CBR)
                }
            }
        encoder.setCallback(Callback(), Handler(thread.looper))
        try {
            encoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        } catch (e: MediaCodec.CodecException) {
            // Some encoders reject the Main profile hint; their default is fine for the PC.
            format.removeKey(MediaFormat.KEY_PROFILE)
            encoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        }
        surface = encoder.createInputSurface()
        encoder.start()
    }

    /** Asks for a key frame now, e.g. after the PC's `RequestKeyFrame` or dropped frames. */
    fun requestKeyFrame() {
        runCatching { encoder.setParameters(Bundle().apply { putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0) }) }
    }

    /** Changes the target bitrate on the fly. */
    fun setBitrate(kbps: Int) {
        if (kbps == bitrateKbps) return
        bitrateKbps = kbps
        runCatching {
            encoder.setParameters(
                Bundle().apply { putInt(MediaCodec.PARAMETER_KEY_VIDEO_BITRATE, kbps * KBPS) },
            )
        }
    }

    fun stop() {
        runCatching { encoder.stop() }
        encoder.release()
        surface.release()
        thread.quitSafely()
    }

    private inner class Callback : MediaCodec.Callback() {
        override fun onInputBufferAvailable(
            codec: MediaCodec,
            index: Int,
        ) = Unit

        override fun onOutputBufferAvailable(
            codec: MediaCodec,
            index: Int,
            info: MediaCodec.BufferInfo,
        ) {
            val buffer = codec.getOutputBuffer(index)
            if (buffer != null && info.size > 0) {
                buffer.position(info.offset).limit(info.offset + info.size)
                val bytes = buffer.toByteString()
                val isConfig = info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
                val isKey = info.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME != 0
                if (isConfig) {
                    config = bytes
                } else {
                    // The PC can join or recover at any key frame: send the config right before it.
                    if (isKey) config?.let { onPacket(packet(it, info.presentationTimeUs, config = true)) }
                    onPacket(packet(bytes, info.presentationTimeUs, key = isKey))
                }
            }
            codec.releaseOutputBuffer(index, false)
        }

        override fun onError(
            codec: MediaCodec,
            e: MediaCodec.CodecException,
        ) {
            Log.w(TAG, "Video encoder error: ${e.diagnosticInfo}")
        }

        override fun onOutputFormatChanged(
            codec: MediaCodec,
            format: MediaFormat,
        ) = Unit
    }

    private fun packet(
        data: ByteString,
        ptsUs: Long,
        key: Boolean = false,
        config: Boolean = false,
    ) = StreamPacket(
        track = StreamTrack.STREAM_TRACK_VIDEO,
        pts_us = ptsUs.coerceAtLeast(0),
        key_frame = key,
        codec_config = config,
        data_ = data,
    )

    companion object {
        private const val TAG = "FenpoMedia"
        private const val KBPS = 1000
        private const val KEY_FRAME_SECONDS = 2
        private const val REPEAT_AFTER_US = 100_000L

        fun mime(codec: VideoCodec): String =
            if (codec ==
                VideoCodec.VIDEO_CODEC_HEVC
            ) {
                MediaFormat.MIMETYPE_VIDEO_HEVC
            } else {
                MediaFormat.MIMETYPE_VIDEO_AVC
            }

        /** The first codec in [accepted] (the PC's order) that this phone can encode; H.264 when the list is empty. */
        fun choose(accepted: List<VideoCodec>): VideoCodec {
            val wanted =
                accepted
                    .filter {
                        it != VideoCodec.VIDEO_CODEC_UNSPECIFIED
                    }.ifEmpty { listOf(VideoCodec.VIDEO_CODEC_H264) }
            return wanted.firstOrNull { canEncode(it) } ?: VideoCodec.VIDEO_CODEC_H264
        }

        private fun canEncode(codec: VideoCodec): Boolean =
            runCatching { MediaCodec.createEncoderByType(mime(codec)).release() }.isSuccess
    }
}
