package com.fenpo.android.core.connection.pcnotifications

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.R
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.PcNotification
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withContext
import java.io.File
import java.security.MessageDigest
import javax.inject.Inject
import javax.inject.Singleton
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * The PC's notifications on the phone (pc_notification.proto): each `PcNotification` becomes a
 * local notification in its own channel, "Notifications from your PC", grouped per PC app, and
 * goes away with `PcNotificationRemoved`. App icons come only over Wi-Fi and are kept per app, so a
 * notification that came over Bluetooth still shows its app's icon. Off with Settings > Features.
 */
@Singleton
class PcNotifications
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val settingsRepository: SettingsRepository,
    ) : PcFeature {
        private val manager = context.getSystemService(NotificationManager::class.java)
        private val icons = File(context.cacheDir, ICON_FOLDER)

        override suspend fun run() {
            messenger.incoming.collect { envelope ->
                when (val body = envelope.body) {
                    is Envelope.Body.PcNotification -> show(body.value)
                    is Envelope.Body.PcNotificationRemoved -> manager.cancel(TAG, idFor(body.value.id))
                    else -> Unit
                }
            }
        }

        private suspend fun show(notification: PcNotification) {
            if (!settingsRepository.settings
                    .first()
                    .features.pcNotifications
            ) {
                return
            }
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.pc_notifications_channel_name),
                    NotificationManager.IMPORTANCE_DEFAULT,
                ).apply { description = context.getString(R.string.pc_notifications_channel_description) },
            )
            val icon = withContext(Dispatchers.IO) { icon(notification) }
            val built =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setSubText(notification.app_name)
                    .setContentTitle(notification.title.ifEmpty { notification.app_name })
                    .setContentText(notification.text)
                    .setStyle(NotificationCompat.BigTextStyle().bigText(notification.text))
                    .setLargeIcon(icon)
                    .setGroup(GROUP_PREFIX + notification.app_id)
                    .setAutoCancel(true)
                    .apply { notification.posted_at?.let { setWhen(it.toEpochMilli()).setShowWhen(true) } }
                    .build()
            manager.notify(TAG, idFor(notification.id), built)
            Log.i(TAG, "PC notification from ${notification.app_name}")
        }

        /** The icon sent with this notification, kept for the app; else the one kept earlier. */
        private fun icon(notification: PcNotification): Bitmap? {
            val file = File(icons, digest(notification.app_id) + ".png")
            val png = notification.app_icon_png
            if (png.size > 0) {
                icons.mkdirs()
                runCatching { file.writeBytes(png.toByteArray()) }
                return BitmapFactory.decodeByteArray(png.toByteArray(), 0, png.size)
            }
            return if (file.exists()) BitmapFactory.decodeFile(file.path) else null
        }

        private fun digest(text: String): String =
            MessageDigest
                .getInstance("SHA-256")
                .digest(text.toByteArray())
                .take(DIGEST_BYTES)
                .joinToString("") { "%02x".format(it) }

        /** Android notification ids are ints; the tag keeps them apart from Fenpo's own. */
        private fun idFor(pcId: String): Int = pcId.hashCode()

        private companion object {
            const val TAG = "FenpoPcNotifications"
            const val CHANNEL_ID = "pc_notifications"
            const val GROUP_PREFIX = "pc:"
            const val ICON_FOLDER = "pc_icons"
            const val DIGEST_BYTES = 12
        }
    }

@Module
@InstallIn(SingletonComponent::class)
internal abstract class PcNotificationsModule {
    @Binds
    @IntoSet
    abstract fun feature(notifications: PcNotifications): PcFeature
}
