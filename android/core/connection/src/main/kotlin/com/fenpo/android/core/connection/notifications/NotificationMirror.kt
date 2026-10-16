package com.fenpo.android.core.connection.notifications

import android.app.ActivityOptions
import android.app.Notification
import android.app.PendingIntent
import android.app.RemoteInput
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.util.Log
import com.fenpo.android.core.connection.LinkPath
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.PcSession
import com.fenpo.android.core.data.notifications.NotificationAppSettings
import com.fenpo.android.core.data.notifications.NotificationAppsRepository
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.protocol.v1.AppIcon
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Error
import com.fenpo.protocol.v1.ErrorCode
import com.fenpo.protocol.v1.NotificationRemoved
import com.fenpo.protocol.v1.NotificationSnapshotDone
import com.fenpo.protocol.v1.PhoneNotification
import com.fenpo.protocol.v1.RunNotificationAction
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.time.Instant
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong
import javax.inject.Inject
import javax.inject.Singleton

/** The last notification that went to the PC, for "Notifications go to your PC" on the home screen. */
data class MirroredNotification(
    val sender: String,
    val at: Instant,
)

/**
 * Sends the phone's notifications to the PC and carries out what the PC asks back: dismiss, press
 * an action, reply (notification.proto, docs/ANDROID.md §6).
 *
 * After every `Hello` the channel gets a snapshot of the current notifications ending with
 * `NotificationSnapshotDone`; then posts and removals go on the best open path. `seq` is
 * time-based, so it keeps growing across process restarts without being stored.
 */
@Singleton
class NotificationMirror
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val apps: NotificationAppsRepository,
        private val settingsRepository: SettingsRepository,
    ) {
        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
        private val converter = NotificationConverter(context)
        private val seq = AtomicLong()
        private val sentKeys = ConcurrentHashMap.newKeySet<String>()
        private val sendLock = Mutex()

        @Volatile private var listener: NotificationListenerService? = null

        @Volatile private var enabled = true

        @Volatile private var appSettings = NotificationAppSettings()

        private val _last = MutableStateFlow<MirroredNotification?>(null)

        /** The last notification sent, or `null` since the app started. */
        val last: StateFlow<MirroredNotification?> = _last.asStateFlow()

        /** Runs while the connection service runs: follows the settings, sessions and PC requests. */
        suspend fun run(): Unit =
            coroutineScope {
                launch { settingsRepository.settings.collect { enabled = it.features.notificationMirroring } }
                launch { apps.settings.collect { appSettings = it } }
                launch { snapshots() }
                messenger.incoming.collect { envelope ->
                    when (val body = envelope.body) {
                        is Envelope.Body.DismissNotification -> dismiss(body.value.key)
                        is Envelope.Body.RunNotificationAction -> runAction(body.value, envelope.id)
                        is Envelope.Body.AppIconRequest -> sendIcon(body.value.app_id)
                        else -> Unit
                    }
                }
            }

        internal fun attach(service: NotificationListenerService) {
            listener = service
        }

        internal fun detach(service: NotificationListenerService) {
            if (listener === service) listener = null
        }

        internal fun onPosted(sbn: StatusBarNotification) {
            if (!shouldMirror(sbn)) return
            scope.launch {
                val update = sbn.key in sentKeys
                apps.recordSeen(
                    sbn.packageName,
                    converter.appName(sbn.packageName),
                    converter.hasReply(sbn.notification),
                )
                if (!appSettings.isEnabled(sbn.packageName)) return@launch
                val notification = converter.convert(sbn, nextSeq(), preExisting = false, update = update)
                if (sendBest(notification)) {
                    sentKeys += sbn.key
                    _last.value =
                        MirroredNotification(notification.title.ifEmpty { notification.app_name }, Instant.now())
                }
            }
        }

        internal fun onRemoved(sbn: StatusBarNotification) {
            if (!sentKeys.remove(sbn.key)) return
            scope.launch {
                val removed = NotificationRemoved(key = sbn.key, seq = nextSeq())
                messenger.send(Envelope.Body.NotificationRemoved(removed), Capability.CAPABILITY_NOTIFICATION_MIRROR)
            }
        }

        /** New sessions get the snapshot; Bluetooth only when Wi-Fi is not open (protocol/README). */
        private suspend fun snapshots() {
            val served = HashSet<PcSession>()
            messenger.sessions.collect { open ->
                served.retainAll(open.values.toSet())
                for ((path, session) in open) {
                    if (session in served) continue
                    served += session
                    if (path == LinkPath.BLUETOOTH && LinkPath.WIFI in open) continue
                    scope.launch { snapshot(session, path) }
                }
            }
        }

        private suspend fun snapshot(
            session: PcSession,
            path: LinkPath,
        ) {
            if (Capability.CAPABILITY_NOTIFICATION_MIRROR !in session.awaitHello().capabilities) return
            val active = if (enabled) listener?.activeNotifications.orEmpty() else emptyArray()
            try {
                var sent = 0
                for (sbn in active) {
                    if (!shouldMirror(sbn) || !appSettings.isEnabled(sbn.packageName)) continue
                    val full = converter.convert(sbn, nextSeq(), preExisting = true, update = false)
                    val notification = if (path == LinkPath.WIFI) full else NotificationConverter.fitForBle(full)
                    sendLock.withLock { session.send(Envelope.Body.PhoneNotification(notification)) }
                    sentKeys += sbn.key
                    sent++
                }
                session.send(Envelope.Body.NotificationSnapshotDone(NotificationSnapshotDone()))
                Log.i(TAG, "Snapshot of $sent notification(s) on $path")
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.d(TAG, "Snapshot on $path cut short: ${e.message}")
            }
        }

        private suspend fun sendBest(notification: PhoneNotification): Boolean =
            sendLock.withLock {
                val body = if (messenger.wifiOpen) notification else NotificationConverter.fitForBle(notification)
                messenger.send(Envelope.Body.PhoneNotification(body), Capability.CAPABILITY_NOTIFICATION_MIRROR)
            }

        private fun shouldMirror(sbn: StatusBarNotification): Boolean {
            if (!enabled || sbn.packageName == context.packageName) return false
            val n = sbn.notification
            if (n.flags and Notification.FLAG_GROUP_SUMMARY != 0) return false
            // Ongoing ones are services, media and downloads, except a ringing call.
            if (sbn.isOngoing && n.category != Notification.CATEGORY_CALL) return false
            return true
        }

        private fun dismiss(key: String) {
            val sbn = listener?.activeNotifications?.firstOrNull { it.key == key } ?: return
            if (sbn.isClearable) listener?.cancelNotification(key)
        }

        private suspend fun runAction(
            request: RunNotificationAction,
            envelopeId: Long,
        ) {
            val sbn = listener?.activeNotifications?.firstOrNull { it.key == request.key }
            val action = sbn?.notification?.actions?.getOrNull(request.action_index)
            if (action == null) {
                messenger.send(
                    Envelope.Body.Error(
                        Error(code = ErrorCode.ERROR_CODE_INVALID_REQUEST, message = "Notification or action is gone"),
                    ),
                    replyTo = envelopeId,
                )
                return
            }
            try {
                val fillIn = Intent()
                val inputs = action.remoteInputs.orEmpty()
                if (request.reply_text.isNotEmpty() && inputs.isNotEmpty()) {
                    val results =
                        Bundle().apply {
                            inputs.forEach {
                                putCharSequence(
                                    it.resultKey,
                                    request.reply_text,
                                )
                            }
                        }
                    RemoteInput.addResultsToIntent(inputs, fillIn, results)
                    RemoteInput.setResultsSource(fillIn, RemoteInput.SOURCE_FREE_FORM_INPUT)
                }
                action.actionIntent.send(context, 0, fillIn, null, null, null, startOptions())
                Log.i(
                    TAG,
                    "Ran \"${action.title}\" for the PC${if (request.reply_text.isNotEmpty()) " with a reply" else ""}",
                )
            } catch (e: PendingIntent.CanceledException) {
                messenger.send(
                    Envelope.Body.Error(
                        Error(code = ErrorCode.ERROR_CODE_INVALID_REQUEST, message = "Action no longer available"),
                    ),
                    replyTo = envelopeId,
                )
            }
        }

        /**
         * Lets an action that opens a screen do so: apps with a companion device association may
         * start activities from the background for actions taken on the paired device.
         */
        private fun startOptions(): Bundle? =
            if (Build.VERSION.SDK_INT >= ANDROID_14) {
                ActivityOptions
                    .makeBasic()
                    .setPendingIntentBackgroundActivityStartMode(ActivityOptions.MODE_BACKGROUND_ACTIVITY_START_ALLOWED)
                    .toBundle()
            } else {
                null
            }

        private suspend fun sendIcon(appId: String) {
            val png = converter.appIcon(appId) ?: return
            // Too large for Bluetooth: the PC asks again once Wi-Fi is up.
            messenger.send(
                Envelope.Body.AppIcon(AppIcon(app_id = appId, png = png)),
                Capability.CAPABILITY_NOTIFICATION_MIRROR,
            )
        }

        private fun nextSeq(): Long = seq.updateAndGet { maxOf(it + 1, System.currentTimeMillis() * MICROS_PER_MILLI) }

        private companion object {
            const val TAG = "FenpoMirror"
            const val ANDROID_14 = 34
            const val MICROS_PER_MILLI = 1000L
        }
    }
