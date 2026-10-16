package com.fenpo.android.core.connection.clipboard

import android.content.Context
import android.content.Intent
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.lifecycle.lifecycleScope
import com.fenpo.android.core.connection.R
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * Invisible activity behind every "Send clipboard": the Quick Settings tile, the connection
 * notification and the home screen. Android lets only the focused app read the clipboard, so this
 * waits for window focus, sends, says what happened and closes (docs/ANDROID.md §8).
 */
@AndroidEntryPoint
class ClipboardSendActivity : ComponentActivity() {
    @Inject lateinit var clipboardSync: ClipboardSync

    private var sent = false

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus || sent) return
        sent = true
        lifecycleScope.launch {
            val message =
                when (clipboardSync.sendCurrent()) {
                    ClipboardSendResult.SENT -> R.string.clipboard_sent
                    ClipboardSendResult.EMPTY -> R.string.clipboard_empty
                    ClipboardSendResult.TOO_LARGE -> R.string.clipboard_too_large
                    ClipboardSendResult.NOT_CONNECTED -> R.string.pc_not_connected
                    ClipboardSendResult.UNSUPPORTED -> R.string.pc_update_needed
                }
            Toast.makeText(applicationContext, message, Toast.LENGTH_SHORT).show()
            finish()
        }
    }

    companion object {
        /** Intent for the activity, usable from a tile, a notification or the app. */
        fun intent(context: Context): Intent =
            Intent(context, ClipboardSendActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
}
