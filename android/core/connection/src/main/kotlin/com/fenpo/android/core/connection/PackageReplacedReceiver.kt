package com.fenpo.android.core.connection

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log

/**
 * After an app update Android kills the process, and the companion device service only wakes
 * Fenpo again when the PC's presence changes. Restart the connection service right away instead
 * (device test, 16 October: three minutes without a connection after reinstalling). It stops by
 * itself when nothing is paired.
 */
class PackageReplacedReceiver : BroadcastReceiver() {
    override fun onReceive(
        context: Context,
        intent: Intent,
    ) {
        if (intent.action != Intent.ACTION_MY_PACKAGE_REPLACED) return
        Log.i("FenpoConnection", "Updated, starting the connection service")
        // Allowed from the background thanks to the companion association.
        runCatching { ConnectionService.start(context) }
            .onFailure { Log.w("FenpoConnection", "Could not start after the update: ${it.message}") }
    }
}
