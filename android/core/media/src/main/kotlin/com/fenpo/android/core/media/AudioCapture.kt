package com.fenpo.android.core.media

import android.Manifest
import android.annotation.SuppressLint
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.media.MediaRecorder
import android.media.projection.MediaProjection
import android.util.Log
import androidx.annotation.RequiresPermission
import com.fenpo.protocol.v1.AudioCodec
import com.fenpo.protocol.v1.StreamPacket
import com.fenpo.protocol.v1.StreamTrack
import okio.ByteString.Companion.toByteString
import kotlin.concurrent.thread

/**
 * Audio for a media connection: the microphone (camera) or the phone's own sound (screen), as
 * 48 kHz 16-bit PCM in 20 ms packets, or AAC-LC when the PC prefers it (protocol/README).
 */
class AudioCapture private constructor(
    private val record: AudioRecord,
    val codec: AudioCodec,
    private val onPacket: (StreamPacket) -> Unit,
) {
    @Volatile private var running = false
    private var worker: Thread? = null

    fun start() {
        running = true
        record.startRecording()
        worker = thread(name = "FenpoAudio") { if (codec == AudioCodec.AUDIO_CODEC_AAC) loopAac() else loopPcm() }
    }

    fun stop() {
        running = false
        worker?.join(JOIN_MS)
        runCatching { record.stop() }
        record.release()
    }

    private fun loopPcm() {
        val buffer = ByteArray(FRAME_BYTES)
        while (running) {
            val read = record.read(buffer, 0, buffer.size)
            if (read <= 0) continue
            onPacket(packet(buffer.copyOf(read).toByteString(), nowUs()))
        }
    }

    private fun loopAac() {
        val encoder = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
        val format =
            MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, SAMPLE_RATE, CHANNELS).apply {
                setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC)
                setInteger(MediaFormat.KEY_BIT_RATE, AAC_BITRATE)
            }
        encoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        encoder.start()
        val info = MediaCodec.BufferInfo()
        val pcm = ByteArray(FRAME_BYTES)
        try {
            while (running) {
                val input = encoder.dequeueInputBuffer(TIMEOUT_US)
                if (input >= 0) {
                    val read = record.read(pcm, 0, pcm.size).coerceAtLeast(0)
                    encoder.getInputBuffer(input)?.apply { clear() }?.put(pcm, 0, read)
                    encoder.queueInputBuffer(input, 0, read, nowUs(), 0)
                }
                var output = encoder.dequeueOutputBuffer(info, 0)
                while (output >= 0) {
                    val buffer = encoder.getOutputBuffer(output)
                    if (buffer != null && info.size > 0) {
                        buffer.position(info.offset).limit(info.offset + info.size)
                        val config = info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
                        onPacket(packet(buffer.toByteString(), info.presentationTimeUs, config))
                    }
                    encoder.releaseOutputBuffer(output, false)
                    output = encoder.dequeueOutputBuffer(info, 0)
                }
            }
        } catch (e: IllegalStateException) {
            Log.w(TAG, "AAC encoder stopped: ${e.message}")
        } finally {
            runCatching { encoder.stop() }
            encoder.release()
        }
    }

    private fun packet(
        data: okio.ByteString,
        ptsUs: Long,
        config: Boolean = false,
    ) = StreamPacket(track = StreamTrack.STREAM_TRACK_AUDIO, pts_us = ptsUs, codec_config = config, data_ = data)

    private fun nowUs() = System.nanoTime() / NANOS_PER_MICRO

    companion object {
        private const val TAG = "FenpoMedia"
        const val SAMPLE_RATE = 48_000
        const val CHANNELS = 1
        private const val FRAME_BYTES = SAMPLE_RATE / 50 * 2 * CHANNELS
        private const val AAC_BITRATE = 96_000
        private const val TIMEOUT_US = 20_000L
        private const val JOIN_MS = 500L
        private const val NANOS_PER_MICRO = 1000L

        /** The PC's first accepted audio codec that this side sends; none when it accepts none. */
        fun choose(accepted: List<AudioCodec>): AudioCodec =
            accepted.firstOrNull { it == AudioCodec.AUDIO_CODEC_PCM_S16LE || it == AudioCodec.AUDIO_CODEC_AAC }
                ?: AudioCodec.AUDIO_CODEC_UNSPECIFIED

        /** The microphone, with Android's echo cancellation for calls. */
        @RequiresPermission(Manifest.permission.RECORD_AUDIO)
        fun microphone(
            codec: AudioCodec,
            onPacket: (StreamPacket) -> Unit,
        ): AudioCapture =
            AudioCapture(
                AudioRecord(MediaRecorder.AudioSource.VOICE_COMMUNICATION, SAMPLE_RATE, MONO, PCM_16, bufferBytes()),
                codec,
                onPacket,
            )

        /** What the phone plays (media, games); apps that forbid capture stay silent. */
        @SuppressLint("MissingPermission") // Checked by the caller before screen capture starts.
        fun playback(
            projection: MediaProjection,
            codec: AudioCodec,
            onPacket: (StreamPacket) -> Unit,
        ): AudioCapture {
            val config =
                AudioPlaybackCaptureConfiguration
                    .Builder(projection)
                    .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
                    .addMatchingUsage(AudioAttributes.USAGE_GAME)
                    .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
                    .build()
            val record =
                AudioRecord
                    .Builder()
                    .setAudioFormat(
                        AudioFormat
                            .Builder()
                            .setEncoding(
                                PCM_16,
                            ).setSampleRate(SAMPLE_RATE)
                            .setChannelMask(MONO)
                            .build(),
                    ).setBufferSizeInBytes(bufferBytes())
                    .setAudioPlaybackCaptureConfig(config)
                    .build()
            return AudioCapture(record, codec, onPacket)
        }

        private const val MONO = AudioFormat.CHANNEL_IN_MONO
        private const val PCM_16 = AudioFormat.ENCODING_PCM_16BIT

        private fun bufferBytes() = maxOf(AudioRecord.getMinBufferSize(SAMPLE_RATE, MONO, PCM_16), FRAME_BYTES * 4)
    }
}
