package com.fenpo.android.feature.camera

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.pm.PackageManager
import android.util.Log
import android.view.Surface
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.protocol.v1.CameraFacing
import com.fenpo.protocol.v1.CameraSettings
import com.fenpo.protocol.v1.CameraSettingsChange
import com.fenpo.protocol.v1.CameraStart
import com.fenpo.protocol.v1.CameraState
import com.fenpo.protocol.v1.CameraStatus
import com.fenpo.protocol.v1.CameraStop
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.VideoFormat
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import javax.inject.Inject
import javax.inject.Singleton
import com.fenpo.android.core.designsystem.R as DesignR

/** What the camera screen shows and the PC was last told. */
data class CameraUiState(
    val state: CameraState = CameraState.CAMERA_STATE_STOPPED,
    val consumer: String = "",
    val settings: CameraSettings =
        CameraSettings(facing = CameraFacing.CAMERA_FACING_BACK, zoom = 1f, center_stage = true),
    val zoomPresets: List<Float> = listOf(1f),
    val frontAvailable: Boolean = true,
    val dimmed: Boolean = false,
)

/** What a running [CameraStreamService] offers the coordinator. */
internal interface CameraStream {
    fun applySettings(settings: CameraSettings)

    fun requestKeyFrame(streamId: String)

    fun stopStream()

    fun setPreview(surface: Surface?)
}

/**
 * The webcam's link to the PC (camera.proto): turns `CameraStart` into the "Start camera"
 * notification (Android opens the camera only after a tap), keeps the camera screen and the PC in
 * step and reports every state change with `CameraStatus`.
 */
@Singleton
class CameraCoordinator
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
    ) : PcFeature {
        private val _ui = MutableStateFlow(CameraUiState())
        val ui: StateFlow<CameraUiState> = _ui.asStateFlow()

        /** The PC's last request, used when the user starts the camera. */
        @Volatile var request: CameraStart? = null
            private set

        @Volatile private var stream: CameraStream? = null

        override suspend fun run() {
            messenger.incoming.collect { envelope ->
                when (val body = envelope.body) {
                    is Envelope.Body.CameraStart -> onStart(body.value)
                    is Envelope.Body.CameraStop -> stream?.stopStream() ?: cancelRequest()
                    is Envelope.Body.CameraSettings -> onPcSettings(body.value)
                    is Envelope.Body.RequestKeyFrame -> stream?.requestKeyFrame(body.value.stream_id)
                    else -> Unit
                }
            }
        }

        private suspend fun onStart(start: CameraStart) {
            request = start
            _ui.update { it.copy(consumer = start.consumer, settings = merged(it.settings, start.settings)) }
            if (stream != null) {
                stream?.applySettings(_ui.value.settings)
                return
            }
            report(CameraState.CAMERA_STATE_WAITING_FOR_TAP)
            showStartNotification(start.consumer)
            Log.i(TAG, "The PC asked for the camera (${start.consumer.ifEmpty { "no app name" }})")
        }

        private fun onPcSettings(change: CameraSettingsChange) {
            _ui.update {
                it.copy(
                    consumer = change.consumer.ifEmpty { it.consumer },
                    settings = merged(it.settings, change.settings),
                )
            }
            stream?.applySettings(_ui.value.settings)
        }

        private suspend fun cancelRequest() {
            request = null
            dismissStartNotification()
            report(CameraState.CAMERA_STATE_STOPPED)
        }

        /** From the camera screen: a change the user made on the phone; the PC hears about it too. */
        suspend fun changeFromPhone(settings: CameraSettings) {
            _ui.update { it.copy(settings = settings) }
            stream?.applySettings(settings)
            messenger.send(
                Envelope.Body.CameraSettings(CameraSettingsChange(settings = settings)),
                Capability.CAPABILITY_CAMERA,
            )
        }

        fun setDimmed(dimmed: Boolean) = _ui.update { it.copy(dimmed = dimmed) }

        /** The camera screen's preview while it is visible; `null` when it goes away. */
        fun setPreview(surface: Surface?) {
            stream?.setPreview(surface)
        }

        /** Whether a stream runs now (the camera screen then only shows it). */
        val streaming: Boolean get() = stream != null

        /** The user stopped the camera on the phone. */
        fun stopFromPhone() {
            stream?.stopStream()
        }

        internal fun attach(running: CameraStream) {
            stream = running
            dismissStartNotification()
        }

        internal fun detach(running: CameraStream) {
            if (stream === running) stream = null
            request = null
        }

        /** Tells the PC and the screen where the camera is. */
        internal suspend fun report(
            state: CameraState,
            streamId: String = "",
            video: VideoFormat? = null,
            session: CameraSession? = null,
            reason: String = "",
        ) {
            _ui.update {
                it.copy(
                    state = state,
                    settings = session?.current() ?: it.settings,
                    zoomPresets = session?.zoomPresets ?: it.zoomPresets,
                    frontAvailable = session?.frontAvailable ?: it.frontAvailable,
                )
            }
            val current = _ui.value
            messenger.send(
                Envelope.Body.CameraStatus(
                    CameraStatus(
                        state = state,
                        stream_id = streamId,
                        settings = current.settings,
                        video = video,
                        zoom_presets = current.zoomPresets,
                        front_available = current.frontAvailable,
                        reason = reason,
                    ),
                ),
                Capability.CAPABILITY_CAMERA,
            )
        }

        private fun showStartNotification(consumer: String) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.camera_channel_name),
                    NotificationManager.IMPORTANCE_HIGH,
                ),
            )
            val open =
                PendingIntent.getActivity(context, 0, CameraActivity.intent(context), PendingIntent.FLAG_IMMUTABLE)
            val title =
                if (consumer.isEmpty()) {
                    context.getString(R.string.camera_request_title)
                } else {
                    context.getString(R.string.camera_request_title_app, consumer)
                }
            val notification =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setContentTitle(title)
                    .setContentText(context.getString(R.string.camera_request_text))
                    .setCategory(NotificationCompat.CATEGORY_CALL)
                    .setPriority(NotificationCompat.PRIORITY_HIGH)
                    .setContentIntent(open)
                    .setAutoCancel(true)
                    .addAction(0, context.getString(R.string.camera_start), open)
                    .build()
            manager.notify(NOTIFICATION_ID, notification)
        }

        private fun dismissStartNotification() {
            context.getSystemService(NotificationManager::class.java).cancel(NOTIFICATION_ID)
        }

        private fun merged(
            current: CameraSettings,
            incoming: CameraSettings?,
        ): CameraSettings {
            if (incoming == null) return current
            return incoming.copy(
                facing =
                    if (incoming.facing ==
                        CameraFacing.CAMERA_FACING_UNSPECIFIED
                    ) {
                        current.facing
                    } else {
                        incoming.facing
                    },
                zoom = if (incoming.zoom > 0f) incoming.zoom else current.zoom,
            )
        }

        private companion object {
            const val TAG = "FenpoCamera"
            const val CHANNEL_ID = "camera"
            const val NOTIFICATION_ID = 4
        }
    }

@Module
@InstallIn(SingletonComponent::class)
internal abstract class CameraModule {
    @Binds
    @IntoSet
    abstract fun feature(coordinator: CameraCoordinator): PcFeature
}
