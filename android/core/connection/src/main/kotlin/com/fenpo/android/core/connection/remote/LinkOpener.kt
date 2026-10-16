package com.fenpo.android.core.connection.remote

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.core.net.toUri
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.R
import com.fenpo.protocol.v1.Envelope
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * "Open on phone" from the PC (`OpenUrl`). Android does not let a background app open a browser
 * (docs/ANDROID.md §6), so the link arrives as a notification that opens it with one tap. Only web
 * links are accepted; anything else could start arbitrary apps.
 */
@Singleton
class LinkOpener
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
    ) {
        private var nextId = FIRST_NOTIFICATION_ID

        /** Handles `OpenUrl` until cancelled; started by the connection service. */
        suspend fun run() {
            messenger.incoming.collect { envelope ->
                val url = (envelope.body as? Envelope.Body.OpenUrl)?.value?.url ?: return@collect
                if (WebLink.isWebLink(url)) notify(url.trim())
            }
        }

        private fun notify(url: String) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.link_channel_name),
                    NotificationManager.IMPORTANCE_HIGH,
                ),
            )
            val id = nextId++
            val open =
                PendingIntent.getActivity(
                    context,
                    id,
                    Intent(Intent.ACTION_VIEW, url.toUri()).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                    PendingIntent.FLAG_IMMUTABLE,
                )
            val notification =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setContentTitle(context.getString(R.string.link_title))
                    .setContentText(url)
                    .setContentIntent(open)
                    .setAutoCancel(true)
                    .addAction(0, context.getString(R.string.link_open), open)
                    .build()
            manager.notify(id, notification)
        }

        private companion object {
            const val CHANNEL_ID = "links"
            const val FIRST_NOTIFICATION_ID = 100
        }
    }

/** Web links in text shared with Fenpo or sent by the PC. */
object WebLink {
    private val FIRST_LINK = Regex("""https?://\S+""", RegexOption.IGNORE_CASE)

    /** `true` for an http or https link with a host. */
    fun isWebLink(url: String): Boolean {
        val uri = runCatching { url.trim().toUri() }.getOrNull() ?: return false
        return uri.scheme?.lowercase() in setOf("http", "https") && !uri.host.isNullOrEmpty()
    }

    /** The first web link in [text], e.g. a page title followed by its address; `null` if none. */
    fun firstIn(text: String): String? =
        FIRST_LINK
            .find(text)
            ?.value
            ?.trimEnd('.', ',', ')', '"')
            ?.takeIf(::isWebLink)
}
