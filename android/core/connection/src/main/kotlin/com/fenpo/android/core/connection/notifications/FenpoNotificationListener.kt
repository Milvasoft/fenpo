package com.fenpo.android.core.connection.notifications

import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

/**
 * Android's notification listener for Fenpo; the user grants it in Settings > Notification access.
 * It only hands notifications to [NotificationMirror], which decides what goes to the PC.
 */
@AndroidEntryPoint
class FenpoNotificationListener : NotificationListenerService() {
    @Inject lateinit var mirror: NotificationMirror

    override fun onListenerConnected() {
        super.onListenerConnected()
        mirror.attach(this)
    }

    override fun onListenerDisconnected() {
        mirror.detach(this)
        super.onListenerDisconnected()
    }

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        mirror.onPosted(sbn)
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification) {
        mirror.onRemoved(sbn)
    }

    companion object {
        /** Class name for permission checks in modules that cannot see this class. */
        const val CLASS_NAME = "com.fenpo.android.core.connection.notifications.FenpoNotificationListener"
    }
}
