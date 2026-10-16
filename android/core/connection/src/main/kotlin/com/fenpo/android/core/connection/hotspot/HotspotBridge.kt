package com.fenpo.android.core.connection.hotspot

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
import com.fenpo.android.core.connection.LinkPath
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.PcSession
import com.fenpo.android.core.connection.R
import com.fenpo.android.core.data.hotspot.HotspotDetails
import com.fenpo.android.core.data.hotspot.HotspotRepository
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.HotspotInfo
import com.fenpo.protocol.v1.HotspotState
import com.fenpo.protocol.v1.HotspotStatus
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import javax.inject.Inject
import javax.inject.Singleton
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * Joining the phone's hotspot from the PC (hotspot.proto). Android lets no app read the hotspot's
 * name and password or turn it on, so the user types them in Settings > Hotspot; they go to the PC
 * after every `Hello` and whenever they change. The PC's `HotspotRequest` shows a notification that
 * opens the hotspot settings.
 */
@Singleton
class HotspotBridge
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val repository: HotspotRepository,
    ) : PcFeature {
        override suspend fun run(): Unit =
            coroutineScope {
                launch { afterHello() }
                launch {
                    repository.details.distinctUntilChanged().drop(1).collect {
                        messenger.send(Envelope.Body.HotspotInfo(info(it)), Capability.CAPABILITY_HOTSPOT)
                    }
                }
                messenger.incoming.collect { envelope ->
                    if (envelope.body is Envelope.Body.HotspotRequest) {
                        val saved = repository.details.first() != null
                        if (saved) askUser()
                        val state =
                            if (saved) HotspotState.HOTSPOT_STATE_ASKED_USER else HotspotState.HOTSPOT_STATE_NOT_SET_UP
                        Log.i(TAG, "The PC wants to join the hotspot: $state")
                        messenger.send(
                            Envelope.Body.HotspotStatus(HotspotStatus(state = state)),
                            Capability.CAPABILITY_HOTSPOT,
                            envelope.id,
                        )
                    }
                }
            }

        /** Every new session gets the details; Bluetooth only when Wi-Fi is not open. */
        private suspend fun afterHello(): Unit =
            coroutineScope {
                val served = HashSet<PcSession>()
                messenger.sessions.collect { open ->
                    served.retainAll(open.values.toSet())
                    for ((path, session) in open) {
                        if (session in served) continue
                        served += session
                        if (path == LinkPath.BLUETOOTH && LinkPath.WIFI in open) continue
                        launch {
                            if (Capability.CAPABILITY_HOTSPOT !in session.awaitHello().capabilities) return@launch
                            try {
                                session.send(Envelope.Body.HotspotInfo(info(repository.details.first())))
                            } catch (e: CancellationException) {
                                throw e
                            } catch (e: Exception) {
                                Log.d(TAG, "Hotspot details not sent on $path: ${e.message}")
                            }
                        }
                    }
                }
            }

        private fun info(details: HotspotDetails?): HotspotInfo =
            if (details == null) {
                HotspotInfo(cleared = true)
            } else {
                HotspotInfo(ssid = details.ssid, passphrase = details.passphrase)
            }

        private fun askUser() {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.hotspot_channel_name),
                    NotificationManager.IMPORTANCE_HIGH,
                ),
            )
            val open =
                PendingIntent.getActivity(
                    context,
                    NOTIFICATION_ID,
                    hotspotSettings().addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                    PendingIntent.FLAG_IMMUTABLE,
                )
            val pcName =
                messenger.sessions.value.values
                    .firstNotNullOfOrNull { it.helloOrNull()?.device_name }
                    .orEmpty()
            val notification =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setContentTitle(context.getString(R.string.hotspot_request_title))
                    .setContentText(context.getString(R.string.hotspot_request_text, pcName))
                    .setContentIntent(open)
                    .setAutoCancel(true)
                    .setTimeoutAfter(TIMEOUT_MS)
                    .addAction(0, context.getString(R.string.hotspot_request_open), open)
                    .build()
            manager.notify(NOTIFICATION_ID, notification)
        }

        /** The tethering screen; it has no public action, so fall back to the network settings. */
        private fun hotspotSettings(): Intent {
            val tether = Intent(TETHER_SETTINGS)
            return if (tether.resolveActivity(context.packageManager) !=
                null
            ) {
                tether
            } else {
                Intent(Settings.ACTION_WIRELESS_SETTINGS)
            }
        }

        private companion object {
            const val TAG = "FenpoHotspot"
            const val CHANNEL_ID = "hotspot"
            const val NOTIFICATION_ID = 40
            const val TIMEOUT_MS = 60_000L
            const val TETHER_SETTINGS = "android.settings.TETHER_SETTINGS"
        }
    }

@Module
@InstallIn(SingletonComponent::class)
internal abstract class HotspotModule {
    @Binds
    @IntoSet
    abstract fun feature(bridge: HotspotBridge): PcFeature
}
