package com.fenpo.android.core.connection.remote

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.pm.PackageManager
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.MediaPlayer
import android.media.RingtoneManager
import android.os.VibrationEffect
import android.os.VibratorManager
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.R
import com.fenpo.protocol.v1.Envelope
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Job
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.time.Duration.Companion.seconds
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * "Find my phone" from the PC (`RingDevice`): the alarm sound at full alarm volume, looping, with
 * vibration, so it is heard in a bag or under a cushion; alarms also play through Do Not Disturb
 * unless the user blocked them. Stops on `RingDevice{stop}`, from the notification, or after a minute.
 */
@Singleton
class RingPlayer
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
    ) {
        private val audio = context.getSystemService(AudioManager::class.java)
        private var player: MediaPlayer? = null
        private var savedVolume: Int? = null
        private var timeout: Job? = null

        /** Handles `RingDevice` until cancelled; started by the connection service. */
        suspend fun run(stopIntent: PendingIntent): Unit =
            coroutineScope {
                try {
                    messenger.incoming.collect { envelope ->
                        val ring = (envelope.body as? Envelope.Body.Ring)?.value ?: return@collect
                        if (ring.stop) {
                            stop()
                        } else {
                            start(stopIntent)
                            timeout?.cancel()
                            timeout =
                                launch {
                                    delay(RING_FOR)
                                    stop()
                                }
                        }
                    }
                } finally {
                    stop()
                }
            }

        private fun start(stopIntent: PendingIntent) {
            if (player != null) return
            val uri =
                RingtoneManager.getActualDefaultRingtoneUri(context, RingtoneManager.TYPE_ALARM)
                    ?: RingtoneManager.getActualDefaultRingtoneUri(context, RingtoneManager.TYPE_RINGTONE)
                    ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION)
            try {
                savedVolume = audio.getStreamVolume(AudioManager.STREAM_ALARM)
                audio.setStreamVolume(AudioManager.STREAM_ALARM, audio.getStreamMaxVolume(AudioManager.STREAM_ALARM), 0)
            } catch (e: SecurityException) {
                Log.w(TAG, "Alarm volume not changed: ${e.message}")
            }
            player =
                MediaPlayer().apply {
                    setAudioAttributes(
                        AudioAttributes
                            .Builder()
                            .setUsage(AudioAttributes.USAGE_ALARM)
                            .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                            .build(),
                    )
                    setDataSource(context, uri)
                    isLooping = true
                    prepare()
                    start()
                }
            context
                .getSystemService(VibratorManager::class.java)
                .defaultVibrator
                .vibrate(VibrationEffect.createWaveform(VIBRATION, 0))
            notify(stopIntent)
            Log.i(TAG, "Ringing for the PC")
        }

        /** Stops the sound, the vibration and the notification. */
        fun stop() {
            timeout?.cancel()
            timeout = null
            val current = player ?: return
            player = null
            runCatching { current.stop() }
            current.release()
            context.getSystemService(VibratorManager::class.java).defaultVibrator.cancel()
            savedVolume?.let { runCatching { audio.setStreamVolume(AudioManager.STREAM_ALARM, it, 0) } }
            savedVolume = null
            context.getSystemService(NotificationManager::class.java).cancel(NOTIFICATION_ID)
        }

        private fun notify(stopIntent: PendingIntent) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.ring_channel_name),
                    NotificationManager.IMPORTANCE_HIGH,
                ).apply { setSound(null, null) },
            )
            val notification =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setContentTitle(context.getString(R.string.ring_title))
                    .setContentText(context.getString(R.string.ring_text))
                    .setCategory(NotificationCompat.CATEGORY_ALARM)
                    .setOngoing(true)
                    .setContentIntent(stopIntent)
                    .addAction(0, context.getString(R.string.ring_stop), stopIntent)
                    .build()
            manager.notify(NOTIFICATION_ID, notification)
        }

        private companion object {
            const val TAG = "FenpoRing"
            const val CHANNEL_ID = "ring"
            const val NOTIFICATION_ID = 3
            val RING_FOR = 60.seconds
            val VIBRATION = longArrayOf(0, 600, 400)
        }
    }
