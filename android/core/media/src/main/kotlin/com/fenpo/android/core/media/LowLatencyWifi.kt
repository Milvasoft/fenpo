package com.fenpo.android.core.media

import android.content.Context
import android.net.wifi.WifiManager

/**
 * Keeps Wi-Fi out of power saving while a stream runs. In power saving the phone's Wi-Fi sleeps
 * between beacons and sends in bursts, which shows as stutter on the PC (device test, 16 October).
 */
class LowLatencyWifi(
    context: Context,
) {
    private val lock =
        context
            .getSystemService(WifiManager::class.java)
            .createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "Fenpo:stream")
            .apply { setReferenceCounted(false) }

    fun acquire() = lock.acquire()

    fun release() {
        if (lock.isHeld) lock.release()
    }
}
