package com.fenpo.android.feature.mirror

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.WindowManager
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.core.content.IntentCompat
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.media.AudioCapture
import com.fenpo.android.core.media.BitrateController
import com.fenpo.android.core.media.LowLatencyWifi
import com.fenpo.android.core.media.MediaStreamSender
import com.fenpo.android.core.media.VideoEncoder
import com.fenpo.protocol.v1.AudioCodec
import com.fenpo.protocol.v1.MediaStreamOpen
import com.fenpo.protocol.v1.ScreenState
import com.fenpo.protocol.v1.StreamKind
import com.fenpo.protocol.v1.VideoCodec
import com.fenpo.protocol.v1.VideoFormat
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.util.UUID
import javax.inject.Inject
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * Mirrors the screen (docs/ANDROID.md §11): a `mediaProjection` foreground service started right
 * after the user's consent; MediaProjection → VirtualDisplay → H.264 → media connection. A rotation
 * resizes the stream on the same connection with a new key frame; when the phone locks, Android
 * ends the capture and the PC hears `PAUSED_LOCKED`.
 */
@AndroidEntryPoint
class MirrorService :
    Service(),
    MirrorStream {
    @Inject lateinit var coordinator: MirrorCoordinator

    @Inject lateinit var messenger: PcMessenger

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val main = Handler(Looper.getMainLooper())
    private var projection: MediaProjection? = null
    private var display: VirtualDisplay? = null
    private var encoder: VideoEncoder? = null
    private var audio: AudioCapture? = null
    private var sender: MediaStreamSender? = null
    private val wifi by lazy { LowLatencyWifi(this) }
    private var bitrate = BitrateController(DEFAULT_KBPS, FLOOR_KBPS)
    private var streamId = ""
    private var codec = VideoCodec.VIDEO_CODEC_H264
    private var maxLongSide = MAX_LONG_SIDE
    private var fps = DEFAULT_FPS
    private var locked = false
    private var started = false

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(
        intent: Intent?,
        flags: Int,
        startId: Int,
    ): Int {
        if (intent?.action == ACTION_STOP) {
            stopStream()
            return START_NOT_STICKY
        }
        if (started || intent == null) return START_NOT_STICKY
        started = true
        // Android 14+: the mediaProjection type must be in the foreground before the projection is taken.
        startForeground(NOTIFICATION_ID, notification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
        val result = intent.getIntExtra(EXTRA_RESULT, 0)
        val data = IntentCompat.getParcelableExtra(intent, EXTRA_DATA, Intent::class.java)
        val granted = data?.let { getSystemService(MediaProjectionManager::class.java).getMediaProjection(result, it) }
        if (granted == null) {
            scope.launch { fail("No screen capture consent") }
            return START_NOT_STICKY
        }
        projection = granted
        coordinator.attach(this)
        scope.launch { stream(granted) }
        return START_NOT_STICKY
    }

    private suspend fun stream(granted: MediaProjection) {
        val request = coordinator.request
        val wanted = request?.max
        codec = VideoEncoder.choose(request?.accepted?.video.orEmpty())
        maxLongSide =
            maxOf(wanted?.width ?: 0, wanted?.height ?: 0).takeIf { it > 0 }?.coerceAtMost(MAX_LONG_SIDE)
                ?: MAX_LONG_SIDE
        fps = (wanted?.fps?.takeIf { it > 0 } ?: DEFAULT_FPS).coerceAtMost(DEFAULT_FPS)
        val target = (wanted?.bitrate_kbps?.takeIf { it > 0 } ?: DEFAULT_KBPS).coerceIn(FLOOR_KBPS, MAX_KBPS)
        bitrate = BitrateController(target, FLOOR_KBPS)
        val audioCodec =
            if (request?.audio == true &&
                hasAudioPermission()
            ) {
                AudioCapture.choose(request.accepted?.audio.orEmpty())
            } else {
                AudioCodec.AUDIO_CODEC_UNSPECIFIED
            }
        streamId = UUID.randomUUID().toString()

        granted.registerCallback(
            object : MediaProjection.Callback() {
                override fun onStop() {
                    // Android ends the capture when the phone locks (15 QPR1+) or the user stops sharing.
                    scope.launch { if (locked) pause() else stopStream() }
                }
            },
            main,
        )
        val format = startEncoder(target)
        val mediaSender =
            MediaStreamSender(
                messenger,
                MediaStreamOpen(
                    stream_id = streamId,
                    kind = StreamKind.STREAM_KIND_SCREEN,
                    video = format,
                    audio = audioCodec,
                    audio_sample_rate =
                        if (audioCodec ==
                            AudioCodec.AUDIO_CODEC_UNSPECIFIED
                        ) {
                            0
                        } else {
                            AudioCapture.SAMPLE_RATE
                        },
                    audio_channels = if (audioCodec == AudioCodec.AUDIO_CODEC_UNSPECIFIED) 0 else AudioCapture.CHANNELS,
                ),
                onCongestion = {
                    encoder?.setBitrate(bitrate.onDrop(SystemClock.elapsedRealtime()))
                    encoder?.requestKeyFrame()
                },
            )
        sender = mediaSender
        val (width, height) = format.width to format.height
        display =
            granted.createVirtualDisplay(
                "Fenpo",
                width,
                height,
                resources.displayMetrics.densityDpi,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                encoder?.surface,
                null,
                main,
            )
        if (audioCodec != AudioCodec.AUDIO_CODEC_UNSPECIFIED) {
            audio = AudioCapture.playback(granted, audioCodec, mediaSender::offer).also { it.start() }
        }
        watchRotation()
        watchLock()
        coordinator.report(ScreenState.SCREEN_STATE_STREAMING, streamId, format)
        scope.launch {
            while (isActive) {
                delay(BITRATE_TICK_MS)
                encoder?.setBitrate(bitrate.onTick(SystemClock.elapsedRealtime()))
            }
        }
        wifi.acquire()
        val ok = mediaSender.run()
        Log.i(TAG, "Screen stream $streamId ended${if (ok) "" else " (connection lost or no Wi-Fi)"}")
        if (sender != null) {
            if (ok) stopStream() else fail("The connection to the PC was lost")
        }
    }

    /** A new encoder for the display's current size and rotation, feeding the same sender. */
    private fun startEncoder(kbps: Int): VideoFormat {
        val bounds = getSystemService(WindowManager::class.java).maximumWindowMetrics.bounds
        val (width, height) = ScreenSize.fit(bounds.width(), bounds.height(), maxLongSide)
        val old = encoder
        encoder = VideoEncoder(codec, width, height, fps, kbps) { packet -> sender?.offer(packet) }
        display?.let {
            it.resize(width, height, resources.displayMetrics.densityDpi)
            it.surface = encoder?.surface
        }
        old?.stop()
        return VideoFormat(codec = codec, width = width, height = height, fps = fps, bitrate_kbps = kbps)
    }

    private var displayListener: DisplayManager.DisplayListener? = null

    private fun watchRotation() {
        val manager = getSystemService(DisplayManager::class.java)
        var rotation = manager.getDisplay(android.view.Display.DEFAULT_DISPLAY)?.rotation
        val listener =
            object : DisplayManager.DisplayListener {
                override fun onDisplayChanged(displayId: Int) {
                    if (displayId != android.view.Display.DEFAULT_DISPLAY) return
                    val now = manager.getDisplay(displayId)?.rotation
                    if (now == rotation || sender == null) return
                    rotation = now
                    val format = startEncoder(bitrate.currentKbps)
                    scope.launch { coordinator.report(ScreenState.SCREEN_STATE_STREAMING, streamId, format) }
                }

                override fun onDisplayAdded(displayId: Int) = Unit

                override fun onDisplayRemoved(displayId: Int) = Unit
            }
        manager.registerDisplayListener(listener, main)
        displayListener = listener
    }

    private val lockReceiver =
        object : BroadcastReceiver() {
            override fun onReceive(
                context: Context,
                intent: Intent,
            ) {
                locked = intent.action == Intent.ACTION_SCREEN_OFF
            }
        }

    private fun watchLock() {
        ContextCompat.registerReceiver(
            this,
            lockReceiver,
            IntentFilter().apply {
                addAction(Intent.ACTION_SCREEN_OFF)
                addAction(Intent.ACTION_USER_PRESENT)
            },
            ContextCompat.RECEIVER_NOT_EXPORTED,
        )
    }

    private suspend fun pause() {
        Log.i(TAG, "Screen locked, capture ended by Android")
        release()
        coordinator.report(ScreenState.SCREEN_STATE_PAUSED_LOCKED, streamId)
        finish()
    }

    override fun requestKeyFrame(streamId: String) {
        if (streamId == this.streamId || streamId.isEmpty()) encoder?.requestKeyFrame()
    }

    override fun stopStream() {
        scope.launch {
            release()
            coordinator.report(ScreenState.SCREEN_STATE_STOPPED, streamId)
            finish()
        }
    }

    private suspend fun fail(reason: String) {
        Log.w(TAG, "Mirroring failed: $reason")
        release()
        coordinator.report(ScreenState.SCREEN_STATE_FAILED, streamId, reason = reason)
        finish()
    }

    private fun release() {
        wifi.release()
        sender?.close()
        sender = null
        audio?.stop()
        audio = null
        display?.release()
        display = null
        encoder?.stop()
        encoder = null
        displayListener?.let { getSystemService(DisplayManager::class.java).unregisterDisplayListener(it) }
        displayListener = null
        runCatching { unregisterReceiver(lockReceiver) }
        projection?.stop()
        projection = null
    }

    private fun finish() {
        coordinator.detach(this)
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    override fun onDestroy() {
        release()
        coordinator.detach(this)
        scope.cancel()
        super.onDestroy()
    }

    private fun hasAudioPermission() =
        ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED

    private fun notification(): Notification {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                getString(R.string.mirror_live_channel_name),
                NotificationManager.IMPORTANCE_LOW,
            ),
        )
        val stop =
            PendingIntent.getService(
                this,
                1,
                Intent(this, MirrorService::class.java).setAction(ACTION_STOP),
                PendingIntent.FLAG_IMMUTABLE,
            )
        return NotificationCompat
            .Builder(this, CHANNEL_ID)
            .setSmallIcon(DesignR.drawable.ic_notification)
            .setContentTitle(getString(R.string.mirror_live_title))
            .setContentText(getString(R.string.mirror_live_text))
            .setOngoing(true)
            .setSilent(true)
            .addAction(0, getString(R.string.mirror_stop), stop)
            .build()
    }

    companion object {
        private const val TAG = "FenpoMirror"
        private const val CHANNEL_ID = "mirror_live"
        private const val NOTIFICATION_ID = 7
        private const val ACTION_STOP = "com.fenpo.android.action.STOP_MIRROR"
        private const val EXTRA_RESULT = "result"
        private const val EXTRA_DATA = "data"
        private const val MAX_LONG_SIDE = 1920
        private const val DEFAULT_FPS = 30
        private const val DEFAULT_KBPS = 6000
        private const val FLOOR_KBPS = 1500
        private const val MAX_KBPS = 8000
        private const val BITRATE_TICK_MS = 2_000L

        /** Starts mirroring with the consent the user just gave. */
        fun start(
            context: Context,
            resultCode: Int,
            data: Intent,
        ) {
            ContextCompat.startForegroundService(
                context,
                Intent(
                    context,
                    MirrorService::class.java,
                ).putExtra(EXTRA_RESULT, resultCode).putExtra(EXTRA_DATA, data),
            )
        }
    }
}

/** The stream size for a display: the long side at most [maxLongSide], both sides even (encoders need it). */
object ScreenSize {
    fun fit(
        width: Int,
        height: Int,
        maxLongSide: Int,
    ): Pair<Int, Int> {
        val scale = minOf(1f, maxLongSide.toFloat() / maxOf(width, height))

        // Round first: 3040 × (1920 / 3040) is 1919.99 in floats.
        fun even(value: Float) = (kotlin.math.round(value).toInt() / 2) * 2
        return even(width * scale) to even(height * scale)
    }
}
