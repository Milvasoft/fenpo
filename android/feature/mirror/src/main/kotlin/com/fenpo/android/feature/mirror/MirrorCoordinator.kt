package com.fenpo.android.feature.mirror

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.provider.Settings
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Error
import com.fenpo.protocol.v1.ErrorCode
import com.fenpo.protocol.v1.ScreenStart
import com.fenpo.protocol.v1.ScreenState
import com.fenpo.protocol.v1.ScreenStatus
import com.fenpo.protocol.v1.VideoFormat
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import javax.inject.Inject
import javax.inject.Singleton
import com.fenpo.android.core.designsystem.R as DesignR

/** What a running [MirrorService] offers the coordinator. */
internal interface MirrorStream {
    fun requestKeyFrame(streamId: String)

    fun stopStream()
}

/**
 * Screen mirroring's link to the PC (screen.proto): turns `ScreenStart` into the "Share screen"
 * notification (Android asks for consent every time), passes the PC's input to the accessibility
 * service and reports every state with `ScreenStatus`.
 */
@Singleton
class MirrorCoordinator
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
    ) : PcFeature {
        /** The PC's last request, used once the user consents. */
        @Volatile var request: ScreenStart? = null
            private set

        @Volatile private var stream: MirrorStream? = null

        override suspend fun run() {
            messenger.incoming.collect { envelope ->
                when (val body = envelope.body) {
                    is Envelope.Body.ScreenStart -> onStart(body.value)
                    is Envelope.Body.ScreenStop -> stream?.stopStream() ?: cancelRequest()
                    is Envelope.Body.ScreenInput -> onInput(body.value, envelope.id)
                    is Envelope.Body.RequestKeyFrame -> stream?.requestKeyFrame(body.value.stream_id)
                    else -> Unit
                }
            }
        }

        private suspend fun onStart(start: ScreenStart) {
            request = start
            if (stream != null) return
            report(ScreenState.SCREEN_STATE_WAITING_FOR_CONSENT)
            showConsentNotification(start.control && !controlAvailable)
            Log.i(TAG, "The PC asked for the screen${if (start.control) " with control" else ""}")
        }

        private suspend fun onInput(
            input: com.fenpo.protocol.v1.ScreenInput,
            envelopeId: Long,
        ) {
            val service = FenpoAccessibilityService.running
            if (stream == null || service == null || !service.perform(input)) {
                messenger.send(
                    Envelope.Body.Error(
                        Error(code = ErrorCode.ERROR_CODE_UNSUPPORTED, message = "Control is not available"),
                    ),
                    replyTo = envelopeId,
                )
            }
        }

        private suspend fun cancelRequest() {
            request = null
            dismissConsentNotification()
            report(ScreenState.SCREEN_STATE_STOPPED)
        }

        val controlAvailable: Boolean get() = FenpoAccessibilityService.running != null

        internal fun attach(running: MirrorStream) {
            stream = running
            dismissConsentNotification()
        }

        internal fun detach(running: MirrorStream) {
            if (stream === running) stream = null
            request = null
        }

        internal suspend fun report(
            state: ScreenState,
            streamId: String = "",
            video: VideoFormat? = null,
            reason: String = "",
        ) {
            messenger.send(
                Envelope.Body.ScreenStatus(
                    ScreenStatus(
                        state = state,
                        stream_id = streamId,
                        video = video,
                        control_available = controlAvailable,
                        reason = reason,
                    ),
                ),
                Capability.CAPABILITY_SCREEN_MIRROR,
            )
        }

        private fun showConsentNotification(askForControl: Boolean) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.mirror_channel_name),
                    NotificationManager.IMPORTANCE_HIGH,
                ),
            )
            val share =
                PendingIntent.getActivity(
                    context,
                    0,
                    MirrorConsentActivity.intent(context),
                    PendingIntent.FLAG_IMMUTABLE,
                )
            val builder =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setContentTitle(context.getString(R.string.mirror_request_title))
                    .setContentText(context.getString(R.string.mirror_request_text))
                    .setPriority(NotificationCompat.PRIORITY_HIGH)
                    .setContentIntent(share)
                    .setAutoCancel(true)
                    .addAction(0, context.getString(R.string.mirror_share), share)
            if (askForControl) {
                // The PC also wants control: one tap to the accessibility switch.
                val settings =
                    PendingIntent.getActivity(
                        context,
                        1,
                        Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                        PendingIntent.FLAG_IMMUTABLE,
                    )
                builder
                    .setStyle(
                        NotificationCompat.BigTextStyle().bigText(
                            context.getString(R.string.mirror_request_control_text),
                        ),
                    ).addAction(0, context.getString(R.string.mirror_allow_control), settings)
            }
            manager.notify(NOTIFICATION_ID, builder.build())
        }

        private fun dismissConsentNotification() {
            context.getSystemService(NotificationManager::class.java).cancel(NOTIFICATION_ID)
        }

        private companion object {
            const val TAG = "FenpoMirror"
            const val CHANNEL_ID = "mirror"
            const val NOTIFICATION_ID = 6
        }
    }

@Module
@InstallIn(SingletonComponent::class)
internal abstract class MirrorModule {
    @Binds
    @IntoSet
    abstract fun feature(coordinator: MirrorCoordinator): PcFeature
}
