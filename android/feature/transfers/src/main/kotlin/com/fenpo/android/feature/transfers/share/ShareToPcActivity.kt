package com.fenpo.android.feature.transfers.share

import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.lifecycle.lifecycleScope
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.remote.WebLink
import com.fenpo.android.feature.transfers.R
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.ClipboardItem
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.OpenUrl
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import java.time.Instant
import java.util.UUID
import javax.inject.Inject
import com.fenpo.android.core.connection.R as ConnectionR

/**
 * The two Fenpo targets in the share sheet (design: "Paylaşım menüsü"), as two aliases of this
 * activity: "Send to PC" puts shared text on the PC's clipboard, "Open on PC" opens a shared link
 * in the PC's browser. Nothing is shown; the result is a short message and the sheet closes.
 */
@AndroidEntryPoint
class ShareToPcActivity : ComponentActivity() {
    @Inject lateinit var messenger: PcMessenger

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val text = intent.takeIf { it.action == Intent.ACTION_SEND }?.getStringExtra(Intent.EXTRA_TEXT)
        val open = intent.component?.className?.endsWith(OPEN_ALIAS) == true
        lifecycleScope.launch {
            val message = if (open) openOnPc(text) else sendToPc(text)
            Toast.makeText(applicationContext, message, Toast.LENGTH_SHORT).show()
            finish()
        }
    }

    private suspend fun openOnPc(text: String?): Int {
        val url = text?.let(WebLink::firstIn) ?: return R.string.share_no_link
        return when {
            messenger.send(
                Envelope.Body.OpenUrl(OpenUrl(url = url)),
                Capability.CAPABILITY_OPEN_URL,
            ) -> R.string.share_opened

            messenger.connected -> ConnectionR.string.pc_update_needed

            else -> ConnectionR.string.pc_not_connected
        }
    }

    private suspend fun sendToPc(text: String?): Int {
        if (text.isNullOrEmpty()) return R.string.share_nothing
        val item =
            ClipboardItem(
                item_id = UUID.randomUUID().toString(),
                created_at = Instant.now(),
                content = ClipboardItem.Content.Text(text),
            )
        return when {
            messenger.send(Envelope.Body.Clipboard(item), Capability.CAPABILITY_CLIPBOARD) -> R.string.share_sent_text
            messenger.connected -> ConnectionR.string.pc_update_needed
            else -> ConnectionR.string.pc_not_connected
        }
    }

    private companion object {
        const val OPEN_ALIAS = ".OpenOnPc"
    }
}
