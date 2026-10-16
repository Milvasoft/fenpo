package com.fenpo.android.core.connection

import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Process
import android.service.notification.StatusBarNotification
import androidx.core.app.NotificationCompat
import androidx.core.app.Person
import androidx.core.app.RemoteInput
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.connection.notifications.NotificationConverter
import com.fenpo.protocol.v1.NotificationCategory
import com.fenpo.protocol.v1.NotificationMessage
import com.fenpo.protocol.v1.PhoneNotification
import okio.ByteString.Companion.toByteString
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
class NotificationConverterTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()

    private fun sbn(notification: Notification) =
        StatusBarNotification(
            context.packageName,
            context.packageName,
            7,
            null,
            0,
            0,
            0,
            notification,
            Process.myUserHandle(),
            1_000,
        )

    @Test
    fun readsAConversationWithAReplyAction() {
        val me = Person.Builder().setName("Ben").build()
        val mert = Person.Builder().setName("Mert").build()
        val reply =
            NotificationCompat.Action
                .Builder(
                    0,
                    "Yanıtla",
                    PendingIntent.getBroadcast(context, 0, Intent("x"), PendingIntent.FLAG_MUTABLE),
                ).addRemoteInput(RemoteInput.Builder("text").build())
                .build()
        val notification =
            NotificationCompat
                .Builder(context, "c")
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setCategory(Notification.CATEGORY_MESSAGE)
                .setStyle(
                    NotificationCompat
                        .MessagingStyle(me)
                        .setConversationTitle("Ekip")
                        .addMessage("Toplantı 3'te", 10, mert)
                        .addMessage("Tamam", 20, null as Person?),
                ).addAction(reply)
                .build()

        val converted =
            NotificationConverter(
                context,
            ).convert(sbn(notification), seq = 5, preExisting = true, update = false)

        assertEquals(NotificationCategory.NOTIFICATION_CATEGORY_SOCIAL, converted.category)
        assertEquals("Ekip", converted.conversation_title)
        assertEquals(listOf("Mert", ""), converted.messages.map { it.sender })
        assertEquals("Yanıtla", converted.actions.single().title)
        assertTrue(converted.actions.single().accepts_reply)
        assertTrue(converted.pre_existing)
        assertEquals(5, converted.seq)
    }

    @Test
    fun mapsAndroidCategories() {
        assertEquals(
            NotificationCategory.NOTIFICATION_CATEGORY_INCOMING_CALL,
            NotificationConverter.category(Notification.CATEGORY_CALL),
        )
        assertEquals(
            NotificationCategory.NOTIFICATION_CATEGORY_MISSED_CALL,
            NotificationConverter.category(Notification.CATEGORY_MISSED_CALL),
        )
        assertEquals(
            NotificationCategory.NOTIFICATION_CATEGORY_SCHEDULE,
            NotificationConverter.category(Notification.CATEGORY_ALARM),
        )
        assertEquals(NotificationCategory.NOTIFICATION_CATEGORY_UNSPECIFIED, NotificationConverter.category(null))
    }

    @Test
    fun shrinksToFitBluetooth() {
        val big =
            PhoneNotification(
                key = "k",
                title = "Mert",
                text = "x".repeat(4000),
                messages = List(5) { NotificationMessage(sender = "Mert", text = "y".repeat(800)) },
                large_icon_png = ByteArray(3000).toByteString(),
            )

        val fitted = NotificationConverter.fitForBle(big)

        assertTrue(PhoneNotification.ADAPTER.encodedSize(fitted) <= NotificationConverter.BLE_BUDGET)
        assertEquals(0, fitted.large_icon_png.size)
        assertEquals("Mert", fitted.title)
        val small = PhoneNotification(key = "k", title = "hi", large_icon_png = ByteArray(100).toByteString())
        assertEquals(small, NotificationConverter.fitForBle(small))
    }
}
