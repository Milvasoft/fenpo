package com.fenpo.android.core.connection.tile

import android.annotation.SuppressLint
import android.app.PendingIntent
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import android.widget.Toast
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.R
import com.fenpo.android.core.connection.clipboard.ClipboardSendActivity
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.LockPc
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import javax.inject.Inject

/** Shared tile behaviour: active while the PC is connected, "Fenpo" as the subtitle (design). */
abstract class FenpoTileService : TileService() {
    @Inject lateinit var messenger: PcMessenger

    protected val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    override fun onStartListening() {
        super.onStartListening()
        qsTile?.apply {
            state = if (messenger.connected) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
            subtitle = getString(R.string.tile_subtitle)
            updateTile()
        }
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }
}

/** "Send clipboard": opens [ClipboardSendActivity], the only way to read the clipboard. */
@AndroidEntryPoint
class ClipboardTileService : FenpoTileService() {
    @SuppressLint("StartActivityAndCollapseDeprecated")
    override fun onClick() {
        val intent = ClipboardSendActivity.intent(this)
        if (Build.VERSION.SDK_INT >= ANDROID_14) {
            startActivityAndCollapse(PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_IMMUTABLE))
        } else {
            @Suppress("DEPRECATION")
            startActivityAndCollapse(intent)
        }
    }

    private companion object {
        const val ANDROID_14 = 34
    }
}

/** "Lock PC": sends `LockPc` on the best open path. */
@AndroidEntryPoint
class LockPcTileService : FenpoTileService() {
    override fun onClick() {
        scope.launch {
            val sent = messenger.send(Envelope.Body.LockPc(LockPc()), Capability.CAPABILITY_LOCK_PC)
            val message =
                when {
                    sent -> R.string.lock_pc_sent
                    messenger.connected -> R.string.pc_update_needed
                    else -> R.string.pc_not_connected
                }
            Toast.makeText(applicationContext, message, Toast.LENGTH_SHORT).show()
        }
    }
}
