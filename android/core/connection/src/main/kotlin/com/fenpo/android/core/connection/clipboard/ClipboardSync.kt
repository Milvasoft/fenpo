package com.fenpo.android.core.connection.clipboard

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.pm.PackageManager
import android.net.Uri
import android.util.Log
import android.util.Patterns
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.R
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.ClipboardImage
import com.fenpo.protocol.v1.ClipboardItem
import com.fenpo.protocol.v1.ClipboardRequest
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Error
import com.fenpo.protocol.v1.ErrorCode
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.withContext
import okio.ByteString.Companion.toByteString
import java.io.ByteArrayOutputStream
import java.io.File
import java.time.Instant
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton
import com.fenpo.android.core.designsystem.R as DesignR

/** What happened to "Send clipboard". */
enum class ClipboardSendResult { SENT, EMPTY, TOO_LARGE, NOT_CONNECTED, UNSUPPORTED }

/**
 * Clipboard between the phone and the PC (docs/ANDROID.md §8). PC → phone is automatic: an item
 * from the PC goes straight to the phone's clipboard, which Android allows from the background.
 * Phone → PC needs a focused activity, because Android only lets the app in focus read the
 * clipboard; [ClipboardSendActivity] calls [sendCurrent].
 */
@Singleton
class ClipboardSync
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
    ) {
        private val clipboard = context.getSystemService(ClipboardManager::class.java)
        private val sentIds = ArrayDeque<String>()
        private val _fromPc = MutableStateFlow<String?>(null)

        /** Start of the last text that came from the PC, for the home screen. */
        val fromPc: StateFlow<String?> = _fromPc.asStateFlow()

        private val _toPc = MutableStateFlow<String?>(null)

        /** Start of the last text the phone sent. */
        val toPc: StateFlow<String?> = _toPc.asStateFlow()

        /** Handles the PC's clipboard messages until cancelled; started by the connection service. */
        suspend fun run() {
            messenger.incoming.collect { envelope ->
                when (val body = envelope.body) {
                    is Envelope.Body.Clipboard -> {
                        receive(body.value)
                    }

                    // Android does not let a background app read the clipboard (§8).
                    is Envelope.Body.ClipboardRequest -> {
                        messenger.send(
                            Envelope.Body.Error(
                                Error(
                                    code = ErrorCode.ERROR_CODE_UNSUPPORTED,
                                    message = "Android does not allow reading the clipboard in the background",
                                ),
                            ),
                            replyTo = envelope.id,
                        )
                    }

                    else -> {
                        Unit
                    }
                }
            }
        }

        /** Asks the PC for its latest item ("Paste from PC"); it arrives through [run]. */
        suspend fun requestFromPc(): Boolean =
            messenger.send(Envelope.Body.ClipboardRequest(ClipboardRequest()), Capability.CAPABILITY_CLIPBOARD)

        /** Reads the clipboard and sends it. Call only while an activity of this app has focus. */
        suspend fun sendCurrent(): ClipboardSendResult {
            if (!messenger.connected) return ClipboardSendResult.NOT_CONNECTED
            if (Capability.CAPABILITY_CLIPBOARD !in messenger.capabilities.value) return ClipboardSendResult.UNSUPPORTED
            val clip = clipboard.primaryClip?.takeIf { it.itemCount > 0 } ?: return ClipboardSendResult.EMPTY
            val item =
                when (val content = read(clip)) {
                    null -> return ClipboardSendResult.EMPTY
                    Content.TooLarge -> return ClipboardSendResult.TOO_LARGE
                    is Content.Ready -> content.item
                }
            remember(item.item_id)
            if (!messenger.send(Envelope.Body.Clipboard(item), Capability.CAPABILITY_CLIPBOARD)) {
                // Connected but neither path took it: an image while only Bluetooth is up.
                val image = item.content is ClipboardItem.Content.Image
                return if (image) ClipboardSendResult.TOO_LARGE else ClipboardSendResult.NOT_CONNECTED
            }
            _toPc.value = item.content.plainText()?.take(PREVIEW_CHARS)
            return ClipboardSendResult.SENT
        }

        private sealed interface Content {
            data class Ready(
                val item: ClipboardItem,
            ) : Content

            data object TooLarge : Content
        }

        private suspend fun read(clip: ClipData): Content? {
            val first = clip.getItemAt(0)
            val id = UUID.randomUUID().toString()
            val now = Instant.now()
            first.uri?.let { uri ->
                val type = context.contentResolver.getType(uri)
                if (type in IMAGE_TYPES) {
                    val bytes = readLimited(uri) ?: return Content.TooLarge
                    return Content.Ready(
                        ClipboardItem(
                            item_id = id,
                            created_at = now,
                            content =
                                ClipboardItem.Content.Image(
                                    ClipboardImage(mime_type = type.orEmpty(), data_ = bytes.toByteString()),
                                ),
                        ),
                    )
                }
            }
            val text = first.coerceToText(context)?.toString()?.takeIf { it.isNotEmpty() } ?: return null
            val isUrl =
                text.trim().let {
                    it.startsWith("http", ignoreCase = true) &&
                        Patterns.WEB_URL.matcher(it).matches()
                }
            return Content.Ready(
                if (isUrl) {
                    ClipboardItem(item_id = id, created_at = now, content = ClipboardItem.Content.Url(text.trim()))
                } else {
                    ClipboardItem(item_id = id, created_at = now, content = ClipboardItem.Content.Text(text))
                },
            )
        }

        /** The image's bytes, or `null` above the protocol's 512 KB clipboard limit. */
        private suspend fun readLimited(uri: Uri): ByteArray? =
            withContext(Dispatchers.IO) {
                // InputStream.readNBytes is API 33; this module runs on 31.
                context.contentResolver.openInputStream(uri)?.use { input ->
                    val out = ByteArrayOutputStream()
                    val buffer = ByteArray(BUFFER_BYTES)
                    while (out.size() <= MAX_IMAGE_BYTES) {
                        val read = input.read(buffer)
                        if (read < 0) return@use out.toByteArray()
                        out.write(buffer, 0, read)
                    }
                    null
                }
            }

        private suspend fun receive(item: ClipboardItem) {
            if (item.item_id in sentIds) return
            val text = item.content.plainText()
            val image = (item.content as? ClipboardItem.Content.Image)?.value
            val clip =
                when {
                    text != null -> ClipData.newPlainText(LABEL, text)
                    image != null -> imageClip(item.item_id, image) ?: return
                    else -> return
                }
            clipboard.setPrimaryClip(clip)
            val preview = text?.take(PREVIEW_CHARS)
            _fromPc.value = preview ?: context.getString(R.string.clipboard_image)
            Log.i(TAG, "Clipboard from the PC: ${if (image != null) "image" else "${preview?.length} chars"}")
            notifyReceived(preview)
        }

        private suspend fun imageClip(
            id: String,
            image: ClipboardImage,
        ): ClipData? {
            val extension =
                when (image.mime_type) {
                    "image/png" -> "png"
                    "image/jpeg" -> "jpg"
                    else -> return null
                }
            val file =
                withContext(Dispatchers.IO) {
                    val folder = File(context.cacheDir, CLIPBOARD_FOLDER).apply { mkdirs() }
                    folder.listFiles()?.forEach { it.delete() }
                    File(folder, "${id.take(ID_CHARS)}.$extension").apply { writeBytes(image.data_.toByteArray()) }
                }
            val uri = FileProvider.getUriForFile(context, "${context.packageName}$AUTHORITY_SUFFIX", file)
            return ClipData.newUri(context.contentResolver, LABEL, uri)
        }

        private fun notifyReceived(preview: String?) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
                PackageManager.PERMISSION_GRANTED
            ) {
                return
            }
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    context.getString(R.string.clipboard_channel_name),
                    NotificationManager.IMPORTANCE_LOW,
                ),
            )
            val notification =
                NotificationCompat
                    .Builder(context, CHANNEL_ID)
                    .setSmallIcon(DesignR.drawable.ic_notification)
                    .setContentTitle(context.getString(R.string.clipboard_received))
                    .setContentText(preview ?: context.getString(R.string.clipboard_image))
                    .setTimeoutAfter(NOTIFICATION_TIMEOUT_MS)
                    .setSilent(true)
                    .build()
            manager.notify(NOTIFICATION_ID, notification)
        }

        private fun ClipboardItem.Content?.plainText(): String? =
            when (this) {
                is ClipboardItem.Content.Text -> value
                is ClipboardItem.Content.Url -> value
                else -> null
            }

        private fun remember(id: String) {
            sentIds.addLast(id)
            while (sentIds.size > SENT_IDS) sentIds.removeFirst()
        }

        private companion object {
            const val TAG = "FenpoClipboard"
            const val LABEL = "Fenpo"
            const val CHANNEL_ID = "clipboard"
            const val NOTIFICATION_ID = 2
            const val NOTIFICATION_TIMEOUT_MS = 120_000L
            const val PREVIEW_CHARS = 80
            const val ID_CHARS = 8
            const val SENT_IDS = 16
            const val CLIPBOARD_FOLDER = "clipboard"
            const val AUTHORITY_SUFFIX = ".files"
            const val BUFFER_BYTES = 16 * 1024

            /** content.proto: larger images travel as a file transfer. */
            const val MAX_IMAGE_BYTES = 512 * 1024
            val IMAGE_TYPES = setOf("image/png", "image/jpeg")
        }
    }
