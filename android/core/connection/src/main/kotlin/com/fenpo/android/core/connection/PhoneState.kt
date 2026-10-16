package com.fenpo.android.core.connection

import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import androidx.core.content.ContextCompat
import com.fenpo.protocol.v1.DeviceState
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged

/**
 * This phone's `DeviceState`: battery, charging and whether Do Not Disturb is on. Battery
 * broadcasts arrive for voltage and temperature too, so only real changes come out.
 */
internal object PhoneState {
    fun states(context: Context): Flow<DeviceState> =
        callbackFlow {
            val notifications = context.getSystemService(NotificationManager::class.java)
            var battery: Intent? = null

            fun emit() {
                trySend(read(battery, notifications.currentInterruptionFilter))
            }
            val receiver =
                object : BroadcastReceiver() {
                    override fun onReceive(
                        context: Context,
                        intent: Intent,
                    ) {
                        if (intent.action == Intent.ACTION_BATTERY_CHANGED) battery = intent
                        emit()
                    }
                }
            val filter =
                IntentFilter().apply {
                    addAction(Intent.ACTION_BATTERY_CHANGED)
                    addAction(NotificationManager.ACTION_INTERRUPTION_FILTER_CHANGED)
                }
            // Battery changes are sticky: the current value comes back right away.
            battery = ContextCompat.registerReceiver(context, receiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
            emit()
            awaitClose { context.unregisterReceiver(receiver) }
        }.distinctUntilChanged()

    /** Builds the state from the last battery broadcast and the current interruption filter. */
    fun read(
        battery: Intent?,
        interruptionFilter: Int,
    ): DeviceState {
        val level = battery?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = battery?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1
        val status = battery?.getIntExtra(BatteryManager.EXTRA_STATUS, -1) ?: -1
        return DeviceState(
            battery_percent = if (level >= 0 && scale > 0) level * PERCENT / scale else null,
            charging =
                battery?.let {
                    status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL
                },
            focus_active =
                when (interruptionFilter) {
                    NotificationManager.INTERRUPTION_FILTER_UNKNOWN -> null
                    NotificationManager.INTERRUPTION_FILTER_ALL -> false
                    else -> true
                },
        )
    }

    private const val PERCENT = 100
}
