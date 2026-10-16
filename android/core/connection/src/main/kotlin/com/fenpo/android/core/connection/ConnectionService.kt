package com.fenpo.android.core.connection

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.ble.BleChannel
import com.fenpo.android.core.ble.BleCounterStore
import com.fenpo.android.core.ble.GattConnection
import com.fenpo.android.core.ble.GattState
import com.fenpo.android.core.data.device.LocalDeviceRepository
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.android.core.transport.ble.BleChannelCrypto
import com.fenpo.android.core.transport.session.Heartbeat
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Pong
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import javax.inject.Inject
import kotlin.time.Duration.Companion.seconds
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * Keeps the Fenpo BLE channel to the paired PC open while the PC is in range (docs/ANDROID.md §2):
 * a `connectedDevice` foreground service with the persistent notification the design turns into
 * something useful. Started by [FenpoCompanionService] on presence, or by the app after pairing.
 *
 * Every connection starts with `Hello`; the PC's `Hello` updates [ConnectionStatus]. The TLS data
 * channel and the features on top come later.
 */
@AndroidEntryPoint
class ConnectionService : Service() {
    @Inject lateinit var pairedPcRepository: PairedPcRepository

    @Inject lateinit var localDeviceRepository: LocalDeviceRepository

    @Inject lateinit var status: ConnectionStatus

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var link: Job? = null
    private var pcName = ""

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(
        intent: Intent?,
        flags: Int,
        startId: Int,
    ): Int {
        if (intent?.action == ACTION_STOP) {
            shutDown()
            return START_NOT_STICKY
        }
        // connectedDevice needs BLUETOOTH_CONNECT at the moment of startForeground.
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_CONNECT) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            Log.w(TAG, "BLUETOOTH_CONNECT missing, not starting")
            stopSelf()
            return START_NOT_STICKY
        }
        createChannel()
        startForeground(
            NOTIFICATION_ID,
            notification(connected = false),
            ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE,
        )
        if (link?.isActive != true) link = scope.launch { runLink() }
        return START_STICKY
    }

    private suspend fun runLink() {
        val pc = pairedPcRepository.pairedPc.first()
        val key = pairedPcRepository.bleChannelKey()
        if (pc == null || key == null || pc.bleAddress.isEmpty()) {
            Log.w(TAG, "No paired PC with a BLE link, stopping")
            shutDown()
            return
        }
        pcName = pc.name
        val adapter = getSystemService(BluetoothManager::class.java).adapter
        val device = adapter.getRemoteDevice(pc.bleAddress)
        val counters = pairedPcRepository.bleCounters()
        val crypto = BleChannelCrypto(key, BleChannelCrypto.Sender.PHONE, counters.nextSend, counters.lastReceived)
        val store =
            object : BleCounterStore {
                override suspend fun saveSendCounter(nextSendCounter: Long) =
                    pairedPcRepository.saveBleSendCounter(nextSendCounter)

                override suspend fun saveReceivedCounter(lastReceivedCounter: Long) =
                    pairedPcRepository.saveBleReceivedCounter(lastReceivedCounter)
            }

        var backoff = 1.seconds
        while (scope.isActive) {
            val gatt = GattConnection(this, device)
            val channel = BleChannel(gatt, crypto, store)
            val heartbeat = Heartbeat { channel.send(it) }
            status.update { it.copy(bluetooth = LinkState.CONNECTING, remote = null) }
            try {
                // autoConnect: waits quietly until the PC is reachable instead of failing.
                channel.open(autoConnect = true)
                status.update { it.copy(bluetooth = LinkState.CONNECTED) }
                notify(connected = true)
                backoff = 1.seconds
                val readers =
                    listOf(
                        scope.launch { channel.incoming.collect { onEnvelope(it, channel, heartbeat) } },
                        scope.launch {
                            while (isActive) {
                                val rssi = gatt.readRssi()
                                status.update { it.copy(rssi = rssi) }
                                delay(RSSI_INTERVAL)
                            }
                        },
                        // Once the PC has spoken the protocol, only answered Pings keep the link (device test A2).
                        scope.launch {
                            status.snapshot.first { it.remote != null }
                            heartbeat.run()
                            Log.w(TAG, "PC stopped answering Ping, reconnecting")
                            gatt.close()
                        },
                    )
                channel.send(Envelope.Body.Hello(localDeviceRepository.localDevice().hello()))
                gatt.state.first { it is GattState.Disconnected }
                readers.forEach { it.cancel() }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "BLE link failed: ${e.message}")
            } finally {
                channel.close()
                status.update { it.copy(bluetooth = LinkState.OFF, rssi = null) }
                if (scope.isActive) notify(connected = false)
            }
            delay(backoff)
            backoff = (backoff * 2).coerceAtMost(MAX_BACKOFF)
        }
    }

    private suspend fun onEnvelope(
        envelope: Envelope,
        channel: BleChannel,
        heartbeat: Heartbeat,
    ) {
        when (val body = envelope.body) {
            is Envelope.Body.Hello -> {
                status.update { it.copy(remote = body.value) }
            }

            is Envelope.Body.Ping -> {
                channel.send(
                    Envelope.Body.Pong(Pong(nonce = body.value.nonce)),
                    replyTo = envelope.id,
                )
            }

            is Envelope.Body.Pong -> {
                heartbeat.onPong(body.value.nonce)
            }

            else -> {
                Log.d(TAG, "Unhandled ${body?.javaClass?.simpleName}")
            }
        }
    }

    private fun shutDown() {
        link?.cancel()
        status.update { it.copy(bluetooth = LinkState.OFF, remote = null, rssi = null) }
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    private fun createChannel() {
        val channel =
            NotificationChannel(
                CHANNEL_ID,
                getString(R.string.connection_channel_name),
                NotificationManager.IMPORTANCE_LOW,
            ).apply { description = getString(R.string.connection_channel_description) }
        getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    private fun notification(connected: Boolean): Notification {
        val launch = packageManager.getLaunchIntentForPackage(packageName)
        val open = launch?.let { PendingIntent.getActivity(this, 0, it, PendingIntent.FLAG_IMMUTABLE) }
        return NotificationCompat
            .Builder(this, CHANNEL_ID)
            .setSmallIcon(DesignR.drawable.ic_notification)
            .setContentTitle(pcName.ifEmpty { null })
            .setContentText(
                getString(if (connected) R.string.connection_connected_bluetooth else R.string.connection_connecting),
            ).setContentIntent(open)
            .setOngoing(true)
            .setSilent(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
    }

    private fun notify(connected: Boolean) {
        getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, notification(connected))
    }

    companion object {
        private const val TAG = "FenpoConnection"
        private const val CHANNEL_ID = "connection"
        private const val NOTIFICATION_ID = 1
        private const val ACTION_STOP = "com.fenpo.android.action.STOP_CONNECTION"
        private val RSSI_INTERVAL = 5.seconds
        private val MAX_BACKOFF = 60.seconds

        /** Starts or keeps the connection service; allowed from the background thanks to the association. */
        fun start(context: Context) {
            ContextCompat.startForegroundService(context, Intent(context, ConnectionService::class.java))
        }

        /** Stops it, e.g. when the PC leaves range. */
        fun stop(context: Context) {
            context.startService(Intent(context, ConnectionService::class.java).setAction(ACTION_STOP))
        }
    }
}
