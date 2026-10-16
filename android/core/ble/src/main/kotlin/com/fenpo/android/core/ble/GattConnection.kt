package com.fenpo.android.core.ble

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothProfile
import android.bluetooth.BluetoothStatusCodes
import android.content.Context
import android.os.Build
import android.util.Log
import com.fenpo.android.core.transport.ble.FenpoBleUuids
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import java.io.IOException
import kotlin.time.Duration
import kotlin.time.Duration.Companion.milliseconds
import kotlin.time.Duration.Companion.seconds

/** Link state of a [GattConnection]. */
sealed interface GattState {
    data object Disconnected : GattState

    data object Connecting : GattState

    /** Ready: services found, MTU set, `tx` notifications on. */
    data class Ready(
        val mtu: Int,
    ) : GattState
}

/** A GATT operation failed or timed out; [status] is the GATT or BluetoothStatusCodes value. */
class GattException(
    message: String,
    val status: Int = 0,
) : IOException(message)

/**
 * The phone's GATT client for the Fenpo service on the PC (docs/ANDROID.md §4).
 *
 * Android allows one outstanding GATT operation; every operation here goes through one lock and
 * waits for its callback. Setup order: connect → discover services → request MTU 517 → write the
 * `tx` CCCD. The CCCD write touches an encrypted characteristic, which makes Android start bonding
 * the first time (the system pairing dialog); its timeout is long for that reason.
 *
 * Callers must hold `BLUETOOTH_CONNECT`.
 */
@SuppressLint("MissingPermission")
class GattConnection(
    private val context: Context,
    private val device: BluetoothDevice,
) {
    private val _state = MutableStateFlow<GattState>(GattState.Disconnected)
    val state: StateFlow<GattState> = _state.asStateFlow()

    private val _notifications =
        MutableSharedFlow<ByteArray>(extraBufferCapacity = 64, onBufferOverflow = BufferOverflow.SUSPEND)

    /** Values notified on `tx`, in order. */
    val notifications: SharedFlow<ByteArray> = _notifications.asSharedFlow()

    private val operations = Mutex()
    private var gatt: BluetoothGatt? = null
    private var pending: CompletableDeferred<Int>? = null
    private var rssiResult: CompletableDeferred<Int?>? = null
    private var connected: CompletableDeferred<Unit>? = null
    private var mtu = DEFAULT_MTU

    private val callback =
        object : BluetoothGattCallback() {
            override fun onConnectionStateChange(
                gatt: BluetoothGatt,
                status: Int,
                newState: Int,
            ) {
                Log.d(TAG, "Connection state $newState, status $status")
                if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                    connected?.complete(Unit)
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                    _state.value = GattState.Disconnected
                    connected?.completeExceptionally(GattException("Disconnected", status))
                    pending?.completeExceptionally(GattException("Disconnected", status))
                }
            }

            override fun onServicesDiscovered(
                gatt: BluetoothGatt,
                status: Int,
            ) {
                pending?.complete(status)
            }

            override fun onMtuChanged(
                gatt: BluetoothGatt,
                mtu: Int,
                status: Int,
            ) {
                if (status == BluetoothGatt.GATT_SUCCESS) this@GattConnection.mtu = mtu
                pending?.complete(status)
            }

            override fun onDescriptorWrite(
                gatt: BluetoothGatt,
                descriptor: BluetoothGattDescriptor,
                status: Int,
            ) {
                pending?.complete(status)
            }

            override fun onCharacteristicWrite(
                gatt: BluetoothGatt,
                characteristic: BluetoothGattCharacteristic,
                status: Int,
            ) {
                pending?.complete(status)
            }

            // Android 13+.
            override fun onCharacteristicChanged(
                gatt: BluetoothGatt,
                characteristic: BluetoothGattCharacteristic,
                value: ByteArray,
            ) {
                if (characteristic.uuid == FenpoBleUuids.TX) _notifications.tryEmit(value)
            }

            // Android 12: the value is only on the characteristic object, which the stack reuses.
            @Deprecated("Replaced by the value overload on Android 13")
            override fun onCharacteristicChanged(
                gatt: BluetoothGatt,
                characteristic: BluetoothGattCharacteristic,
            ) {
                @Suppress("DEPRECATION")
                val value = characteristic.value
                if (Build.VERSION.SDK_INT < ANDROID_13 && characteristic.uuid == FenpoBleUuids.TX && value != null) {
                    _notifications.tryEmit(value.copyOf())
                }
            }

            override fun onReadRemoteRssi(
                gatt: BluetoothGatt,
                rssi: Int,
                status: Int,
            ) {
                rssiResult?.complete(rssi.takeIf { status == BluetoothGatt.GATT_SUCCESS })
            }

            override fun onServiceChanged(gatt: BluetoothGatt) {
                // The PC republished its service; old handles are invalid (S1 lesson). Reconnect.
                Log.i(TAG, "Service changed, reconnecting")
                gatt.disconnect()
            }
        }

    /**
     * Connects and prepares the channel.
     *
     * @param autoConnect `false` for the first, foreground connection (fast, gives up); `true` for
     *   background reconnection (waits until the PC is in range).
     * @return the negotiated ATT MTU.
     */
    suspend fun connect(autoConnect: Boolean): Int {
        close()
        _state.value = GattState.Connecting
        val ready = CompletableDeferred<Unit>().also { connected = it }

        @Suppress("DEPRECATION") // The settings-based connectGatt is API 37; minSdk is 31.
        val gatt = device.connectGatt(context, autoConnect, callback, BluetoothDevice.TRANSPORT_LE)
        this.gatt = gatt
        try {
            if (autoConnect) ready.await() else withTimeout(CONNECT_TIMEOUT) { ready.await() }
            run("discover services", SETUP_TIMEOUT) { gatt.discoverServices() }.check("discover services")
            // Android 14+ always asks for 517 on the first request; the result is min(517, PC).
            run("request MTU", SETUP_TIMEOUT) { gatt.requestMtu(MAX_MTU) }
            val tx = characteristic(FenpoBleUuids.TX)
            check(gatt.setCharacteristicNotification(tx, true)) { "setCharacteristicNotification failed" }
            val cccd = tx.getDescriptor(FenpoBleUuids.CCCD) ?: throw GattException("tx has no CCCD")
            run("enable notifications", BOND_TIMEOUT) {
                gatt.writeDescriptorCompat(cccd, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE)
            }.check("enable notifications")
            _state.value = GattState.Ready(mtu)
            return mtu
        } catch (e: Exception) {
            close()
            throw e
        }
    }

    /** Writes one fragment to `rx` without response; retries while the stack is busy. */
    suspend fun write(fragment: ByteArray) {
        val gatt = gatt ?: throw GattException("Not connected")
        val rx = characteristic(FenpoBleUuids.RX)
        run("write rx", SETUP_TIMEOUT) {
            var result = gatt.writeWithoutResponseCompat(rx, fragment)
            var attempts = 0
            while (result == WriteResult.BUSY && attempts++ < BUSY_RETRIES) {
                delay(BUSY_DELAY)
                result = gatt.writeWithoutResponseCompat(rx, fragment)
            }
            result == WriteResult.STARTED
        }.check("write rx")
    }

    /** Signal strength of the open link in dBm (for the proximity lock, §4), or `null` if unavailable. */
    suspend fun readRssi(): Int? {
        val gatt = gatt ?: return null
        return operations.withLock {
            val result = CompletableDeferred<Int?>().also { rssiResult = it }
            try {
                if (gatt.readRemoteRssi()) withTimeout(SETUP_TIMEOUT) { result.await() } else null
            } finally {
                rssiResult = null
            }
        }
    }

    /** Disconnects and releases the GATT client; required before reconnecting (error 133 otherwise). */
    fun close() {
        gatt?.let {
            it.disconnect()
            it.close()
        }
        gatt = null
        pending?.cancel()
        connected?.cancel()
        _state.value = GattState.Disconnected
    }

    private fun characteristic(uuid: java.util.UUID): BluetoothGattCharacteristic =
        gatt?.getService(FenpoBleUuids.SERVICE)?.getCharacteristic(uuid)
            ?: throw GattException("Fenpo service or $uuid not found")

    /** Starts one operation and waits for its callback status. */
    private suspend fun run(
        name: String,
        timeout: Duration,
        start: suspend () -> Boolean,
    ): Int =
        operations.withLock {
            val result = CompletableDeferred<Int>().also { pending = it }
            if (!start()) throw GattException("$name could not start")
            try {
                withTimeout(timeout) { result.await() }
            } finally {
                pending = null
            }
        }

    private enum class WriteResult { STARTED, BUSY, FAILED }

    /** Android 13 returns a status code; Android 12 only says whether the write started. */
    private fun BluetoothGatt.writeWithoutResponseCompat(
        characteristic: BluetoothGattCharacteristic,
        value: ByteArray,
    ): WriteResult {
        val type = BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE
        if (Build.VERSION.SDK_INT >= ANDROID_13) {
            return when (writeCharacteristic(characteristic, value, type)) {
                BluetoothStatusCodes.SUCCESS -> WriteResult.STARTED
                BluetoothStatusCodes.ERROR_GATT_WRITE_REQUEST_BUSY -> WriteResult.BUSY
                else -> WriteResult.FAILED
            }
        }
        characteristic.writeType = type
        @Suppress("DEPRECATION")
        characteristic.value = value
        // On Android 12 a refused write almost always means the previous one is still in flight.
        @Suppress("DEPRECATION")
        return if (writeCharacteristic(characteristic)) WriteResult.STARTED else WriteResult.BUSY
    }

    private fun BluetoothGatt.writeDescriptorCompat(
        descriptor: BluetoothGattDescriptor,
        value: ByteArray,
    ): Boolean {
        if (Build.VERSION.SDK_INT >=
            ANDROID_13
        ) {
            return writeDescriptor(descriptor, value) == BluetoothStatusCodes.SUCCESS
        }
        @Suppress("DEPRECATION")
        descriptor.value = value
        @Suppress("DEPRECATION")
        return writeDescriptor(descriptor)
    }

    private fun Int.check(name: String) {
        if (this != BluetoothGatt.GATT_SUCCESS) throw GattException("$name failed", this)
    }

    private companion object {
        const val TAG = "FenpoGatt"
        const val ANDROID_13 = 33
        const val DEFAULT_MTU = 23
        const val MAX_MTU = 517
        const val BUSY_RETRIES = 50
        val BUSY_DELAY = 10.milliseconds
        val CONNECT_TIMEOUT = 30.seconds
        val SETUP_TIMEOUT = 10.seconds

        // Long enough for the user to read and accept the system pairing dialog.
        val BOND_TIMEOUT = 90.seconds
    }
}
