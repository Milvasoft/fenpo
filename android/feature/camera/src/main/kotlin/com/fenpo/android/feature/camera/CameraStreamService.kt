package com.fenpo.android.feature.camera

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.os.IBinder
import android.os.SystemClock
import android.util.Log
import android.view.Surface
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.media.AudioCapture
import com.fenpo.android.core.media.BitrateController
import com.fenpo.android.core.media.MediaStreamSender
import com.fenpo.android.core.media.VideoEncoder
import com.fenpo.protocol.v1.AudioCodec
import com.fenpo.protocol.v1.CameraSettings
import com.fenpo.protocol.v1.CameraState
import com.fenpo.protocol.v1.MediaStreamOpen
import com.fenpo.protocol.v1.StreamKind
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
 * Runs the webcam (docs/ANDROID.md §10): a `camera` (and `microphone`) foreground service, started
 * from the camera screen because Android allows that type only while the app is visible. It keeps
 * streaming after the screen is left or dimmed, until the PC or the user stops it.
 */
@AndroidEntryPoint
class CameraStreamService :
    Service(),
    CameraStream {
    @Inject lateinit var coordinator: CameraCoordinator

    @Inject lateinit var messenger: PcMessenger

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var session: CameraSession? = null
    private var encoder: VideoEncoder? = null
    private var audio: AudioCapture? = null
    private var sender: MediaStreamSender? = null
    private var streamId = ""
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
        if (started) return START_NOT_STICKY
        started = true
        val microphone = coordinator.ui.value.settings.microphone && hasMicrophonePermission()
        val types =
            ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA or
                if (microphone) ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE else 0
        startForeground(NOTIFICATION_ID, notification(), types)
        coordinator.attach(this)
        scope.launch { stream(microphone) }
        return START_NOT_STICKY
    }

    private suspend fun stream(microphone: Boolean) {
        val request = coordinator.request
        val wanted = request?.video
        val codec = VideoEncoder.choose(request?.accepted?.video.orEmpty())
        val width = (wanted?.width?.takeIf { it > 0 } ?: DEFAULT_WIDTH).coerceAtMost(MAX_WIDTH)
        val height = (wanted?.height?.takeIf { it > 0 } ?: DEFAULT_HEIGHT).coerceAtMost(MAX_HEIGHT)
        val fps = (wanted?.fps?.takeIf { it > 0 } ?: DEFAULT_FPS).coerceAtMost(DEFAULT_FPS)
        val bitrate = BitrateController(TARGET_KBPS, FLOOR_KBPS)
        val audioCodec =
            if (microphone) {
                AudioCapture.choose(
                    request?.accepted?.audio.orEmpty(),
                )
            } else {
                AudioCodec.AUDIO_CODEC_UNSPECIFIED
            }
        streamId = UUID.randomUUID().toString()
        val video = VideoFormat(codec = codec, width = width, height = height, fps = fps, bitrate_kbps = TARGET_KBPS)

        lateinit var videoEncoder: VideoEncoder
        val mediaSender =
            MediaStreamSender(
                messenger,
                MediaStreamOpen(
                    stream_id = streamId,
                    kind = StreamKind.STREAM_KIND_CAMERA,
                    video = video,
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
                    videoEncoder.setBitrate(bitrate.onDrop(SystemClock.elapsedRealtime()))
                    videoEncoder.requestKeyFrame()
                },
            )
        videoEncoder = VideoEncoder(codec, width, height, fps, TARGET_KBPS, mediaSender::offer)
        encoder = videoEncoder
        sender = mediaSender
        val camera = CameraSession(this, videoEncoder.surface, fps) { reason -> scope.launch { fail(reason) } }
        session = camera
        camera.start(coordinator.ui.value.settings)
        if (audioCodec != AudioCodec.AUDIO_CODEC_UNSPECIFIED && hasMicrophonePermission()) {
            @Suppress("MissingPermission")
            audio = AudioCapture.microphone(audioCodec, mediaSender::offer).also { it.start() }
        }
        coordinator.report(CameraState.CAMERA_STATE_STARTING, streamId, video, camera)
        scope.launch {
            delay(STREAMING_AFTER_MS)
            if (sender != null) coordinator.report(CameraState.CAMERA_STATE_STREAMING, streamId, video, camera)
        }
        scope.launch {
            while (isActive) {
                delay(BITRATE_TICK_MS)
                videoEncoder.setBitrate(bitrate.onTick(SystemClock.elapsedRealtime()))
            }
        }
        val ok = mediaSender.run()
        Log.i(TAG, "Camera stream $streamId ended${if (ok) "" else " (connection lost or no Wi-Fi)"}")
        if (sender != null) {
            if (ok) stopStream() else fail("The connection to the PC was lost")
        }
    }

    override fun setPreview(surface: Surface?) {
        session?.setPreview(surface)
    }

    override fun applySettings(settings: CameraSettings) {
        session?.apply(settings) ?: return
        scope.launch {
            delay(SETTINGS_REPORT_MS)
            coordinator.report(CameraState.CAMERA_STATE_STREAMING, streamId, null, session)
        }
    }

    override fun requestKeyFrame(streamId: String) {
        if (streamId == this.streamId || streamId.isEmpty()) encoder?.requestKeyFrame()
    }

    override fun stopStream() {
        scope.launch {
            release()
            coordinator.report(CameraState.CAMERA_STATE_STOPPED, streamId)
            finish()
        }
    }

    private suspend fun fail(reason: String) {
        Log.w(TAG, "Camera failed: $reason")
        release()
        coordinator.report(CameraState.CAMERA_STATE_FAILED, streamId, reason = reason)
        finish()
    }

    private fun release() {
        sender?.close()
        sender = null
        audio?.stop()
        audio = null
        session?.stop()
        session = null
        encoder?.stop()
        encoder = null
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

    private fun hasMicrophonePermission() =
        ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED

    private fun notification(): Notification {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                getString(R.string.camera_live_channel_name),
                NotificationManager.IMPORTANCE_LOW,
            ),
        )
        val open = PendingIntent.getActivity(this, 0, CameraActivity.intent(this), PendingIntent.FLAG_IMMUTABLE)
        val stop =
            PendingIntent.getService(
                this,
                1,
                Intent(this, CameraStreamService::class.java).setAction(ACTION_STOP),
                PendingIntent.FLAG_IMMUTABLE,
            )
        return NotificationCompat
            .Builder(this, CHANNEL_ID)
            .setSmallIcon(DesignR.drawable.ic_notification)
            .setContentTitle(getString(R.string.camera_live_title))
            .setContentText(
                coordinator.ui.value.consumer
                    .ifEmpty { getString(R.string.camera_live_text) },
            ).setContentIntent(open)
            .setOngoing(true)
            .setSilent(true)
            .addAction(0, getString(R.string.camera_stop), stop)
            .build()
    }

    companion object {
        private const val TAG = "FenpoCamera"
        private const val CHANNEL_ID = "camera_live"
        private const val NOTIFICATION_ID = 5
        private const val ACTION_STOP = "com.fenpo.android.action.STOP_CAMERA"
        private const val DEFAULT_WIDTH = 1920
        private const val DEFAULT_HEIGHT = 1080
        private const val MAX_WIDTH = 1920
        private const val MAX_HEIGHT = 1080
        private const val DEFAULT_FPS = 30
        private const val TARGET_KBPS = 6000
        private const val FLOOR_KBPS = 1500
        private const val BITRATE_TICK_MS = 2_000L
        private const val STREAMING_AFTER_MS = 1_500L
        private const val SETTINGS_REPORT_MS = 500L

        /** Starts the stream; call only from the visible camera screen (camera FGS rule). */
        fun start(context: Context) {
            ContextCompat.startForegroundService(context, Intent(context, CameraStreamService::class.java))
        }

        fun stop(context: Context) {
            context.startService(Intent(context, CameraStreamService::class.java).setAction(ACTION_STOP))
        }
    }
}
