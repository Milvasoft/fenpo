package com.fenpo.android.core.connection

import android.companion.AssociationInfo
import android.companion.CompanionDeviceService
import android.companion.DevicePresenceEvent
import android.util.Log
import androidx.annotation.RequiresApi

/**
 * Bound by the system while the associated PC is in range (docs/ANDROID.md §1). Fenpo does not need
 * to be running: the system starts it here when the PC's advertisement appears, and the
 * association allows starting the foreground connection service from the background.
 */
class FenpoCompanionService : CompanionDeviceService() {
    // Android 16+; the system calls this instead of onDeviceAppeared there.
    @RequiresApi(ANDROID_16)
    override fun onDevicePresenceEvent(event: DevicePresenceEvent) {
        Log.i(TAG, "Presence event ${event.event} for association ${event.associationId}")
        when (event.event) {
            DevicePresenceEvent.EVENT_BLE_APPEARED, DevicePresenceEvent.EVENT_BT_CONNECTED -> {
                ConnectionService.start(
                    this,
                )
            }

            DevicePresenceEvent.EVENT_BLE_DISAPPEARED -> {
                ConnectionService.stop(this)
            }

            DevicePresenceEvent.EVENT_ASSOCIATION_REMOVED -> {
                ConnectionService.stop(this)
            }
        }
    }

    // Android 13–15.
    @RequiresApi(ANDROID_13)
    @Deprecated("Replaced by onDevicePresenceEvent on Android 16")
    override fun onDeviceAppeared(associationInfo: AssociationInfo) {
        Log.i(TAG, "PC appeared: association ${associationInfo.id}")
        ConnectionService.start(this)
    }

    @RequiresApi(ANDROID_13)
    @Deprecated("Replaced by onDevicePresenceEvent on Android 16")
    override fun onDeviceDisappeared(associationInfo: AssociationInfo) {
        Log.i(TAG, "PC disappeared: association ${associationInfo.id}")
        ConnectionService.stop(this)
    }

    // Android 12.
    @Deprecated("Replaced by the AssociationInfo overload on Android 13")
    override fun onDeviceAppeared(address: String) {
        Log.i(TAG, "PC appeared at $address")
        ConnectionService.start(this)
    }

    @Deprecated("Replaced by the AssociationInfo overload on Android 13")
    override fun onDeviceDisappeared(address: String) {
        Log.i(TAG, "PC disappeared at $address")
        ConnectionService.stop(this)
    }

    private companion object {
        const val TAG = "FenpoPresence"
        const val ANDROID_13 = 33
        const val ANDROID_16 = 36
    }
}
