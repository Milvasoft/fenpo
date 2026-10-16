package com.fenpo.android.core.connection.notifications

import android.app.Notification
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.drawable.Drawable
import android.service.notification.StatusBarNotification
import androidx.core.app.NotificationCompat
import com.fenpo.protocol.v1.NotificationAction
import com.fenpo.protocol.v1.NotificationCategory
import com.fenpo.protocol.v1.NotificationMessage
import com.fenpo.protocol.v1.PhoneNotification
import okio.ByteString
import okio.ByteString.Companion.toByteString
import java.io.ByteArrayOutputStream
import java.time.Instant

/** Turns Android notifications into `PhoneNotification` (notification.proto, docs/ANDROID.md §6). */
internal class NotificationConverter(
    private val context: Context,
) {
    private val packageManager = context.packageManager
    private val appNames = HashMap<String, String>()

    fun convert(
        sbn: StatusBarNotification,
        seq: Long,
        preExisting: Boolean,
        update: Boolean,
    ): PhoneNotification {
        val n = sbn.notification
        val extras = n.extras
        val style = NotificationCompat.MessagingStyle.extractMessagingStyleFromNotification(n)
        val messages =
            style
                ?.messages
                ?.takeLast(MAX_MESSAGES)
                ?.map {
                    NotificationMessage(
                        sender =
                            it.person
                                ?.name
                                ?.toString()
                                .orEmpty(),
                        text = it.text?.toString().orEmpty(),
                        sent_at = Instant.ofEpochMilli(it.timestamp),
                    )
                }.orEmpty()
        return PhoneNotification(
            key = sbn.key,
            app_id = sbn.packageName,
            app_name = appName(sbn.packageName),
            posted_at = Instant.ofEpochMilli(sbn.postTime),
            title =
                (
                    extras.getCharSequence(Notification.EXTRA_TITLE_BIG)
                        ?: extras.getCharSequence(Notification.EXTRA_TITLE)
                )?.toString()
                    .orEmpty(),
            text =
                (
                    extras.getCharSequence(Notification.EXTRA_BIG_TEXT)
                        ?: extras.getCharSequence(Notification.EXTRA_TEXT)
                )?.toString()
                    .orEmpty()
                    .take(MAX_TEXT),
            conversation_title = style?.conversationTitle?.toString().orEmpty(),
            messages = messages,
            actions = actions(n),
            dismissible = sbn.isClearable,
            large_icon_png = largeIcon(n),
            silent = update && n.flags and Notification.FLAG_ONLY_ALERT_ONCE != 0,
            category = category(n.category),
            pre_existing = preExisting,
            seq = seq,
        )
    }

    /** `true` when an action of [n] has a free-form reply box. */
    fun hasReply(n: Notification): Boolean = actions(n).any { it.accepts_reply }

    /** The app's icon as a 64×64 PNG, or `null` if the app is not visible to Fenpo. */
    fun appIcon(appId: String): ByteString? =
        try {
            png(packageManager.getApplicationIcon(appId), APP_ICON_PX)
        } catch (_: PackageManager.NameNotFoundException) {
            null
        }

    fun appName(appId: String): String =
        appNames.getOrPut(appId) {
            try {
                packageManager.getApplicationLabel(packageManager.getApplicationInfo(appId, 0)).toString()
            } catch (_: PackageManager.NameNotFoundException) {
                appId
            }
        }

    private fun actions(n: Notification): List<NotificationAction> =
        n.actions
            .orEmpty()
            .withIndex()
            .filter { (_, action) -> !action.title.isNullOrBlank() }
            .map { (index, action) ->
                NotificationAction(
                    index = index,
                    title = action.title.toString(),
                    accepts_reply = action.remoteInputs.orEmpty().any { it.allowFreeFormInput },
                )
            }

    private fun largeIcon(n: Notification): ByteString =
        runCatching { n.getLargeIcon()?.loadDrawable(context)?.let { png(it, LARGE_ICON_PX) } }.getOrNull()
            ?: ByteString.EMPTY

    private fun png(
        drawable: Drawable,
        size: Int,
    ): ByteString {
        val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        drawable.setBounds(0, 0, size, size)
        drawable.draw(Canvas(bitmap))
        val out = ByteArrayOutputStream()
        bitmap.compress(Bitmap.CompressFormat.PNG, PNG_QUALITY, out)
        bitmap.recycle()
        return out.toByteArray().toByteString()
    }

    companion object {
        private const val MAX_MESSAGES = 5
        private const val MAX_TEXT = 4000
        private const val LARGE_ICON_PX = 96
        private const val APP_ICON_PX = 64
        private const val PNG_QUALITY = 100

        /** Largest encoded notification that still fits a sealed BLE message with its envelope. */
        const val BLE_BUDGET = 3800

        /** Android's `Notification.category` to the protocol's categories (protocol/README). */
        fun category(category: String?): NotificationCategory =
            when (category) {
                Notification.CATEGORY_CALL -> {
                    NotificationCategory.NOTIFICATION_CATEGORY_INCOMING_CALL
                }

                Notification.CATEGORY_MISSED_CALL -> {
                    NotificationCategory.NOTIFICATION_CATEGORY_MISSED_CALL
                }

                Notification.CATEGORY_MESSAGE, Notification.CATEGORY_SOCIAL -> {
                    NotificationCategory.NOTIFICATION_CATEGORY_SOCIAL
                }

                Notification.CATEGORY_EMAIL -> {
                    NotificationCategory.NOTIFICATION_CATEGORY_EMAIL
                }

                Notification.CATEGORY_EVENT, Notification.CATEGORY_REMINDER, Notification.CATEGORY_ALARM -> {
                    NotificationCategory.NOTIFICATION_CATEGORY_SCHEDULE
                }

                Notification.CATEGORY_NAVIGATION -> {
                    NotificationCategory.NOTIFICATION_CATEGORY_LOCATION
                }

                else -> {
                    NotificationCategory.NOTIFICATION_CATEGORY_UNSPECIFIED
                }
            }

        /**
         * Shrinks [notification] to fit the BLE channel: the large icon goes first, then the text
         * and the conversation get shorter (protocol/README "Notification mirroring").
         */
        fun fitForBle(notification: PhoneNotification): PhoneNotification {
            var fitted = notification
            if (PhoneNotification.ADAPTER.encodedSize(fitted) <= BLE_BUDGET) return fitted
            fitted = fitted.copy(large_icon_png = ByteString.EMPTY)
            for ((text, messages, each) in listOf(Triple(1000, 3, 300), Triple(300, 2, 120), Triple(100, 1, 60))) {
                if (PhoneNotification.ADAPTER.encodedSize(fitted) <= BLE_BUDGET) return fitted
                fitted =
                    fitted.copy(
                        text = fitted.text.take(text),
                        messages = fitted.messages.takeLast(messages).map { it.copy(text = it.text.take(each)) },
                    )
            }
            return fitted.copy(
                actions = fitted.actions.take(2),
                conversation_title = fitted.conversation_title.take(60),
            )
        }
    }
}
