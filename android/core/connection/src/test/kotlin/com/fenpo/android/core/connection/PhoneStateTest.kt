package com.fenpo.android.core.connection

import android.app.NotificationManager
import android.content.Intent
import android.os.BatteryManager
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.protocol.v1.DeviceState
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
class PhoneStateTest {
    private fun battery(
        level: Int,
        scale: Int,
        status: Int,
    ) = Intent(Intent.ACTION_BATTERY_CHANGED)
        .putExtra(BatteryManager.EXTRA_LEVEL, level)
        .putExtra(BatteryManager.EXTRA_SCALE, scale)
        .putExtra(BatteryManager.EXTRA_STATUS, status)

    @Test
    fun readsBatteryChargingAndDoNotDisturb() {
        assertEquals(
            DeviceState(battery_percent = 57, charging = true, focus_active = false),
            PhoneState.read(
                battery(57, 100, BatteryManager.BATTERY_STATUS_CHARGING),
                NotificationManager.INTERRUPTION_FILTER_ALL,
            ),
        )
        assertEquals(
            DeviceState(battery_percent = 40, charging = false, focus_active = true),
            PhoneState.read(
                battery(80, 200, BatteryManager.BATTERY_STATUS_DISCHARGING),
                NotificationManager.INTERRUPTION_FILTER_PRIORITY,
            ),
        )
    }

    @Test
    fun fullOnTheChargerCountsAsCharging() {
        val state =
            PhoneState.read(
                battery(100, 100, BatteryManager.BATTERY_STATUS_FULL),
                NotificationManager.INTERRUPTION_FILTER_ALL,
            )
        assertEquals(true, state.charging)
    }

    @Test
    fun unknownValuesStayUnset() {
        assertEquals(
            DeviceState(),
            PhoneState.read(null, NotificationManager.INTERRUPTION_FILTER_UNKNOWN),
        )
    }
}
