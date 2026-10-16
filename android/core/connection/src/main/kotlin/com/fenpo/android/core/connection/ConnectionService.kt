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
import android.os.SystemClock
import android.util.Log
import android.widget.Toast
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.fenpo.android.core.ble.BleChannel
import com.fenpo.android.core.ble.BleCounterStore
import com.fenpo.android.core.ble.GattConnection
import com.fenpo.android.core.ble.GattState
import com.fenpo.android.core.connection.clipboard.ClipboardSendActivity
import com.fenpo.android.core.connection.clipboard.ClipboardSync
import com.fenpo.android.core.connection.media.MediaBridge
import com.fenpo.android.core.connection.notifications.NotificationMirror
import com.fenpo.android.core.connection.remote.LinkOpener
import com.fenpo.android.core.connection.remote.RingPlayer
import com.fenpo.android.core.connection.transfer.PcShareShortcut
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.android.core.data.device.LocalDeviceRepository
import com.fenpo.android.core.data.pc.PairedPc
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.security.IdentityProvider
import com.fenpo.android.core.transport.ble.BleChannelCrypto
import com.fenpo.android.core.transport.tls.TlsConnector
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.LockPc
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onStart
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import okio.ByteString
import javax.inject.Inject
import kotlin.time.Duration.Companion.seconds
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * Keeps the paired PC connected while it is in range (docs/ANDROID.md §2): a `connectedDevice`
 * foreground service with the persistent notification the design turns into something useful.
 * Started by [FenpoCompanionService] on presence, or by the app after pairing.
 *
 * Runs the Fenpo BLE channel (③) and the Wi-Fi TLS channel (④) side by side; either one keeps the
 * phone connected. Every connection starts with `Hello`. The phone's [PhoneState] goes on the
 * best open path.
 */
@AndroidEntryPoint
class ConnectionService : Service() {
    @Inject lateinit var pairedPcRepository: PairedPcRepository

    @Inject lateinit var localDeviceRepository: LocalDeviceRepository

    @Inject lateinit var identityProvider: IdentityProvider

    @Inject lateinit var status: ConnectionStatus

    @Inject lateinit var messenger: PcMessenger

    @Inject lateinit var clipboardSync: ClipboardSync

    @Inject lateinit var ringPlayer: RingPlayer

    @Inject lateinit var linkOpener: LinkOpener

    @Inject lateinit var transferManager: TransferManager

    @Inject lateinit var notificationMirror: NotificationMirror

    @Inject lateinit var settingsRepository: SettingsRepository

    @Inject lateinit var mediaBridge: MediaBridge

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var link: Job? = null
    private var pcName = ""
    private var wifiLink: WifiLink? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(
        intent: Intent?,
        flags: Int,
        startId: Int,
    ): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                shutDown()
                return START_NOT_STICKY
            }

            ACTION_STOP_RING -> {
                ringPlayer.stop()
                return START_STICKY
            }

            ACTION_CANCEL_TRANSFER -> {
                intent.getStringExtra(EXTRA_TRANSFER_ID)?.let(transferManager::cancel)
                return START_STICKY
            }

            ACTION_LOCK_PC -> {
                scope.launch { lockPc() }
                if (link?.isActive == true) return START_STICKY
            }
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
            notification(bluetooth = false, wifi = false),
            ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE,
        )
        if (link?.isActive != true) link = scope.launch { runLinks() }
        return START_STICKY
    }

    private suspend fun runLinks() =
        coroutineScope {
            val pc = pairedPcRepository.pairedPc.first()
            if (pc == null) {
                Log.w(TAG, "No paired PC, stopping")
                shutDown()
                return@coroutineScope
            }
            pcName = pc.name
            PcShareShortcut.publish(this@ConnectionService, pc.name)
            val local = localDeviceRepository.localDevice()
            val hello = local.hello()
            launch { showLinks() }
            launch { reportPhoneState() }
            launch { clipboardSync.run() }
            launch { ringPlayer.run(serviceIntent(ACTION_STOP_RING)) }
            launch { linkOpener.run() }
            transferManager.cancelIntentFor = { id ->
                PendingIntent.getService(
                    this@ConnectionService,
                    id.hashCode(),
                    Intent(this@ConnectionService, ConnectionService::class.java)
                        .setAction(ACTION_CANCEL_TRANSFER)
                        .putExtra(EXTRA_TRANSFER_ID, id),
                    PendingIntent.FLAG_IMMUTABLE,
                )
            }
            launch { transferManager.run() }
            launch { notificationMirror.run() }
            launch { mediaBridge.run() }
            launch { reportMedia() }

            val wifi =
                WifiLink(
                    this@ConnectionService,
                    pc,
                    TlsConnector(identityProvider.identity(local.id)),
                    hello,
                    status,
                    pairedPcRepository,
                    onOther = messenger::deliver,
                ) { onSession(LinkPath.WIFI, it) }
            wifiLink = wifi
            launch { wifi.run() }

            val key = pairedPcRepository.bleChannelKey()
            if (pc.bleAddress.isNotEmpty() && key != null) {
                launch { runBle(pc, key, hello) }
            } else {
                Log.i(TAG, "No BLE link yet, Wi-Fi only")
            }
        }

    private suspend fun runBle(
        pc: PairedPc,
        key: ByteArray,
        hello: Hello,
    ) {
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
            val session =
                PcSession(
                    LinkPath.BLUETOOTH,
                    status,
                    transport = { body, replyTo -> channel.send(body, replyTo = replyTo) },
                    onAddresses = { addresses ->
                        pairedPcRepository.saveAddresses(addresses)
                        wifiLink?.retryNow()
                    },
                    onOther = messenger::deliver,
                )
            status.update { it.copy(bluetooth = LinkState.CONNECTING) }
            try {
                // autoConnect: waits quietly until the PC is reachable instead of failing.
                channel.open(autoConnect = true)
                status.update { it.copy(bluetooth = LinkState.CONNECTED) }
                Log.i(TAG, "BLE link open")
                backoff = 1.seconds
                val readers =
                    listOf(
                        scope.launch { channel.incoming.collect { session.onEnvelope(it) } },
                        scope.launch {
                            while (isActive) {
                                val rssi = gatt.readRssi()
                                status.update { it.copy(rssi = rssi) }
                                delay(RSSI_INTERVAL)
                            }
                        },
                        // Once the PC has spoken the protocol, only answered Pings keep the link (device test A2).
                        scope.launch {
                            session.awaitHello()
                            onSession(LinkPath.BLUETOOTH, session)
                            session.heartbeat.run()
                            Log.w(TAG, "PC stopped answering Ping, reconnecting")
                            gatt.close()
                        },
                    )
                channel.send(Envelope.Body.Hello(hello))
                gatt.state.first { it is GattState.Disconnected }
                readers.forEach { it.cancel() }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "BLE link failed: ${e.message}")
            } finally {
                onSession(LinkPath.BLUETOOTH, null)
                channel.close()
                status.update { it.copy(bluetooth = LinkState.OFF, rssi = null) }
            }
            delay(backoff)
            backoff = (backoff * 2).coerceAtMost(MAX_BACKOFF)
        }
    }

    /** Keeps the sessions that finished `Hello`; what the PC said goes once no path is left. */
    private fun onSession(
        path: LinkPath,
        session: PcSession?,
    ) {
        val now = messenger.onSession(path, session)
        if (now.isEmpty()) status.update { it.copy(remote = null, pcStatus = null) }
    }

    /** Sends battery, charging and Do Not Disturb on the best open path: Wi-Fi, else Bluetooth. */
    private suspend fun reportPhoneState() {
        val preferred = messenger.sessions.map { it[LinkPath.WIFI] ?: it[LinkPath.BLUETOOTH] }.distinctUntilChanged()
        // "Sync Do Not Disturb" off: the PC does not learn about it.
        val dnd = settingsRepository.settings.map { it.features.doNotDisturbSync }.distinctUntilChanged()
        val rssi = rssiReports().onStart { emit(null) }
        combine(PhoneState.states(this), dnd, rssi, preferred) { phone, syncDnd, dbm, session ->
            (if (syncDnd) phone else phone.copy(focus_active = null)).copy(ble_rssi_dbm = dbm) to session
        }.collect { (state, session) ->
            if (session == null) return@collect
            if (Capability.CAPABILITY_DEVICE_STATE !in session.awaitHello().capabilities) return@collect
            try {
                session.send(Envelope.Body.DeviceState(state))
                Log.d(
                    TAG,
                    "DeviceState on ${session.path}: ${state.battery_percent}%, charging ${state.charging}, focus ${state.focus_active}",
                )
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.d(TAG, "DeviceState not sent on ${session.path}: ${e.message}")
            }
        }
    }

    /** The PC's signal as this phone measures it, for the proximity lock (protocol/README "Proximity"). */
    private fun rssiReports(): Flow<Int?> {
        val gate = RssiGate()
        return flow {
            while (true) {
                val dbm = status.snapshot.value.rssi
                if (dbm != null && gate.accept(dbm, SystemClock.elapsedRealtime())) emit(dbm)
                delay(RSSI_INTERVAL)
            }
        }
    }

    /** Now playing on the best open path; artwork only on Wi-Fi, so a move to Wi-Fi sends it again. */
    private suspend fun reportMedia() {
        val preferred = messenger.sessions.map { it[LinkPath.WIFI] ?: it[LinkPath.BLUETOOTH] }.distinctUntilChanged()
        combine(mediaBridge.states(), preferred, ::Pair).collect { (state, session) ->
            Log.d(TAG, "Media: ${if (state.active) "${state.app_id}, playing ${state.playing}" else "nothing"}")
            if (session == null) return@collect
            if (Capability.CAPABILITY_MEDIA !in session.awaitHello().capabilities) return@collect
            val body = if (session.path == LinkPath.WIFI) state else state.copy(artwork_jpeg = ByteString.EMPTY)
            try {
                session.send(Envelope.Body.MediaState(body))
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.d(TAG, "MediaState not sent on ${session.path}: ${e.message}")
            }
        }
    }

    /** The persistent notification follows the open paths. */
    private suspend fun showLinks() {
        status.snapshot
            .map { (it.bluetooth == LinkState.CONNECTED) to (it.wifi == LinkState.CONNECTED) }
            .distinctUntilChanged()
            .collect { (bluetooth, wifi) -> notify(bluetooth, wifi) }
    }

    private fun shutDown() {
        link?.cancel()
        messenger.clear()
        status.update { ConnectionSnapshot() }
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

    private fun notification(
        bluetooth: Boolean,
        wifi: Boolean,
    ): Notification {
        val launch = packageManager.getLaunchIntentForPackage(packageName)
        val open = launch?.let { PendingIntent.getActivity(this, 0, it, PendingIntent.FLAG_IMMUTABLE) }
        val text =
            when {
                bluetooth && wifi -> R.string.connection_connected_both
                bluetooth -> R.string.connection_connected_bluetooth
                wifi -> R.string.connection_connected_wifi
                else -> R.string.connection_connecting
            }
        val builder =
            NotificationCompat
                .Builder(this, CHANNEL_ID)
                .setSmallIcon(DesignR.drawable.ic_notification)
                .setContentTitle(pcName.ifEmpty { null })
                .setContentText(getString(text))
                .setContentIntent(open)
                .setOngoing(true)
                .setSilent(true)
                .setCategory(NotificationCompat.CATEGORY_SERVICE)
        // The two shortcuts from the design ("Bildirimler ve Hızlı Ayarlar"), only while connected.
        if (bluetooth || wifi) {
            val clipboard =
                PendingIntent.getActivity(this, 1, ClipboardSendActivity.intent(this), PendingIntent.FLAG_IMMUTABLE)
            builder
                .addAction(0, getString(R.string.connection_action_send_clipboard), clipboard)
                .addAction(0, getString(R.string.connection_action_lock_pc), serviceIntent(ACTION_LOCK_PC))
        }
        return builder.build()
    }

    private suspend fun lockPc() {
        val sent = messenger.send(Envelope.Body.LockPc(LockPc()), Capability.CAPABILITY_LOCK_PC)
        val message =
            when {
                sent -> R.string.lock_pc_sent
                messenger.connected -> R.string.pc_update_needed
                else -> R.string.pc_not_connected
            }
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show()
    }

    private fun serviceIntent(action: String): PendingIntent =
        PendingIntent.getService(
            this,
            action.hashCode(),
            Intent(this, ConnectionService::class.java).setAction(action),
            PendingIntent.FLAG_IMMUTABLE,
        )

    private fun notify(
        bluetooth: Boolean,
        wifi: Boolean,
    ) {
        getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, notification(bluetooth, wifi))
    }

    companion object {
        private const val TAG = "FenpoConnection"
        private const val CHANNEL_ID = "connection"
        private const val NOTIFICATION_ID = 1
        private const val ACTION_STOP = "com.fenpo.android.action.STOP_CONNECTION"
        private const val ACTION_STOP_RING = "com.fenpo.android.action.STOP_RING"
        private const val ACTION_LOCK_PC = "com.fenpo.android.action.LOCK_PC"
        private const val ACTION_CANCEL_TRANSFER = "com.fenpo.android.action.CANCEL_TRANSFER"
        private const val EXTRA_TRANSFER_ID = "transfer_id"
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
