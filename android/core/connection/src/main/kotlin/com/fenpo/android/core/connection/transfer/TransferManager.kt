package com.fenpo.android.core.connection.transfer

import android.app.PendingIntent
import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.os.ParcelFileDescriptor
import android.os.StatFs
import android.provider.MediaStore
import android.provider.OpenableColumns
import android.util.Log
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.data.transfer.TransferHistory
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.android.core.data.transfer.TransferRecord
import com.fenpo.android.core.transport.TransferStream
import com.fenpo.android.core.transport.tls.DataConnection
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.FileInfo
import com.fenpo.protocol.v1.TransferAccept
import com.fenpo.protocol.v1.TransferCancel
import com.fenpo.protocol.v1.TransferOffer
import com.fenpo.protocol.v1.TransferProgress
import com.fenpo.protocol.v1.TransferState
import com.fenpo.protocol.v1.TransferStreamHeader
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import okio.HashingSink
import okio.blackholeSink
import okio.buffer
import okio.sink
import okio.source
import java.io.File
import java.io.FileInputStream
import java.io.IOException
import java.time.Instant
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.time.Duration.Companion.seconds

/** A transfer that is still moving bytes. */
data class ActiveTransfer(
    val id: String,
    val incoming: Boolean,
    val fileName: String,
    val fileCount: Int,
    val bytesDone: Long,
    val totalBytes: Long,
)

/** Why "Send to PC" did not start. */
enum class SendResult { STARTED, NOTHING_TO_SEND, NOT_CONNECTED, NEEDS_WIFI, UNSUPPORTED }

/**
 * Files between the phone and the PC (transfer.proto, docs/ANDROID.md §9). Negotiation runs on the
 * control channel; the bytes move on a separate TLS data connection that the phone always opens,
 * in both directions, to the PC's address on Wi-Fi. Received files go to `Download/Fenpo` through
 * MediaStore, which needs no storage permission. Runs inside the connection service.
 */
@Singleton
class TransferManager
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val history: TransferHistory,
    ) {
        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        private val accepts = ConcurrentHashMap<String, CompletableDeferred<TransferAccept>>()
        private val outcomes = ConcurrentHashMap<String, CompletableDeferred<TransferState>>()
        private val jobs = ConcurrentHashMap<String, Job>()
        private val notifications = TransferNotifications(context)
        private val _active = MutableStateFlow<List<ActiveTransfer>>(emptyList())

        /** Transfers in progress, for the Transfers tab and the notification. */
        val active: StateFlow<List<ActiveTransfer>> = _active.asStateFlow()

        /** Set by the connection service: the notification's "Cancel" goes through it. */
        var cancelIntentFor: (String) -> PendingIntent? = { null }

        /** Handles the PC's transfer messages until cancelled; started by the connection service. */
        suspend fun run() {
            messenger.incoming.collect { envelope ->
                when (val body = envelope.body) {
                    is Envelope.Body.TransferOffer -> {
                        jobs[body.value.transfer_id] = scope.launch { receive(body.value) }
                    }

                    is Envelope.Body.TransferAccept -> {
                        accepts.remove(body.value.transfer_id)?.complete(body.value)
                    }

                    is Envelope.Body.TransferProgress -> {
                        onProgress(body.value)
                    }

                    is Envelope.Body.TransferCancel -> {
                        Log.i(TAG, "The PC cancelled ${body.value.transfer_id}")
                        jobs.remove(body.value.transfer_id)?.cancel()
                    }

                    else -> {
                        Unit
                    }
                }
            }
        }

        /**
         * Starts sending [uris] to the PC. The files are opened here, while the caller (a share
         * target) still holds the read grant; the transfer itself continues in the background.
         */
        suspend fun send(uris: List<Uri>): SendResult {
            if (uris.isEmpty()) return SendResult.NOTHING_TO_SEND
            if (!messenger.connected) return SendResult.NOT_CONNECTED
            if (Capability.CAPABILITY_FILE_TRANSFER !in messenger.capabilities.value) return SendResult.UNSUPPORTED
            if (!messenger.wifiOpen) return SendResult.NEEDS_WIFI
            val files = withContext(Dispatchers.IO) { uris.mapNotNull(::openOutgoing) }
            if (files.isEmpty()) return SendResult.NOTHING_TO_SEND
            val id = UUID.randomUUID().toString()
            jobs[id] = scope.launch { sendFiles(id, files) }
            return SendResult.STARTED
        }

        /** Stops a transfer from this side, e.g. the notification's "Cancel". */
        fun cancel(id: String) {
            scope.launch { messenger.send(Envelope.Body.TransferCancel(TransferCancel(transfer_id = id))) }
            jobs.remove(id)?.cancel()
        }

        private class Outgoing(
            val info: FileInfo,
            val descriptor: ParcelFileDescriptor,
            val temp: File? = null,
        )

        private fun openOutgoing(uri: Uri): Outgoing? {
            val resolver = context.contentResolver
            var descriptor = runCatching { resolver.openFileDescriptor(uri, "r") }.getOrNull() ?: return null
            var name: String? = null
            resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { c ->
                if (c.moveToFirst()) name = c.getString(0)
            }
            var temp: File? = null
            if (descriptor.statSize < 0) {
                // A pipe (Samsung Gallery's share provider): its length is unknown and the provider's SIZE
                // column may describe a different file, so copy it first and offer the real size.
                temp = File(File(context.cacheDir, OUTGOING_FOLDER).apply { mkdirs() }, UUID.randomUUID().toString())
                val copied =
                    runCatching {
                        ParcelFileDescriptor.AutoCloseInputStream(descriptor).use { input ->
                            temp.outputStream().use { input.copyTo(it) }
                        }
                    }
                if (copied.isFailure) {
                    temp.delete()
                    return null
                }
                descriptor = ParcelFileDescriptor.open(temp, ParcelFileDescriptor.MODE_READ_ONLY)
            }
            val size = descriptor.statSize
            val info =
                FileInfo(
                    file_id = UUID.randomUUID().toString(),
                    name = FileNames.safe(name ?: uri.lastPathSegment.orEmpty()),
                    size = size,
                    mime_type = resolver.getType(uri) ?: "application/octet-stream",
                )
            return Outgoing(info, descriptor, temp)
        }

        private suspend fun sendFiles(
            id: String,
            files: List<Outgoing>,
        ) {
            val total = files.sumOf { it.info.size }
            val first = files.first().info.name
            var outcome = TransferOutcome.FAILED
            var data: DataConnection? = null
            track(ActiveTransfer(id, incoming = false, first, files.size, 0, total))
            try {
                val accepted = CompletableDeferred<TransferAccept>().also { accepts[id] = it }
                val done = CompletableDeferred<TransferState>().also { outcomes[id] = it }
                val offer = TransferOffer(transfer_id = id, files = files.map { it.info }, total_bytes = total)
                if (!messenger.send(Envelope.Body.TransferOffer(offer), Capability.CAPABILITY_FILE_TRANSFER)) {
                    Log.w(TAG, "Offer $id not sent")
                    return
                }
                // The PC may ask its user first.
                val accept = withTimeout(ACCEPT_TIMEOUT) { accepted.await() }
                if (!accept.accepted) {
                    outcome = TransferOutcome.DECLINED
                    return
                }
                data =
                    messenger.openDataConnection(accept.data_port) ?: run {
                        Log.w(TAG, "No Wi-Fi for the data connection of $id")
                        return
                    }
                TransferStream.writeHeader(data.sink, TransferStreamHeader(transfer_id = id))
                var sent = 0L
                for (file in files) {
                    FileInputStream(file.descriptor.fileDescriptor).source().buffer().use { source ->
                        val before = sent
                        TransferStream.copyExactly(source, data.sink, file.info.size) { n -> progress(id, before + n) }
                        sent += file.info.size
                    }
                }
                // Close only after the PC confirmed. Closing a socket with unread input sends a TCP reset,
                // which made the PC lose the last bytes of larger files (device test, 16 October): the
                // PC's TLS 1.3 session tickets arrive on this connection and are never read. Drain them.
                val connection = data
                val drain = scope.launch { runCatching { connection.source.readAll(blackholeSink()) } }
                val state =
                    try {
                        withTimeout(CONFIRM_TIMEOUT) { done.await() }
                    } finally {
                        drain.cancel()
                    }
                outcome =
                    when (state) {
                        TransferState.TRANSFER_STATE_COMPLETED -> TransferOutcome.COMPLETED
                        TransferState.TRANSFER_STATE_CANCELLED -> TransferOutcome.CANCELLED
                        else -> TransferOutcome.FAILED
                    }
                if (outcome != TransferOutcome.COMPLETED) Log.w(TAG, "The PC reported $state for $id after $sent bytes")
            } catch (e: CancellationException) {
                outcome = TransferOutcome.CANCELLED
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "Sending $id failed: ${e.message}")
            } finally {
                data?.close()
                files.forEach {
                    runCatching { it.descriptor.close() }
                    it.temp?.delete()
                }
                accepts.remove(id)
                outcomes.remove(id)
                finish(TransferRecord(id, first, files.size, incoming = false, Instant.now(), total, outcome))
            }
        }

        private suspend fun receive(offer: TransferOffer) {
            val id = offer.transfer_id
            val files = offer.files
            if (files.isEmpty() || !messenger.wifiOpen || !hasRoomFor(offer.total_bytes)) {
                Log.i(TAG, "Declining $id: ${if (messenger.wifiOpen) "no room" else "no Wi-Fi"}")
                messenger.send(Envelope.Body.TransferAccept(TransferAccept(transfer_id = id, accepted = false)))
                return
            }
            val first = FileNames.safe(files.first().name)
            val written = mutableListOf<Uri>()
            var outcome = TransferOutcome.FAILED
            var data: DataConnection? = null
            track(ActiveTransfer(id, incoming = true, first, files.size, 0, offer.total_bytes))
            try {
                messenger.send(
                    Envelope.Body.TransferAccept(TransferAccept(transfer_id = id, accepted = true, data_port = 0)),
                )
                // The PC fails the transfer when the phone has not connected within 30 s.
                data = withTimeout(CONNECT_TIMEOUT) { messenger.openDataConnection(0) } ?: return
                data.setReadTimeout(READ_TIMEOUT)
                TransferStream.writeHeader(data.sink, TransferStreamHeader(transfer_id = id))
                var received = 0L
                for (file in files) {
                    val uri =
                        createDownload(FileNames.safe(file.name), file.mime_type)
                            ?: throw IOException("No room in Downloads")
                    written += uri
                    val output = context.contentResolver.openOutputStream(uri) ?: throw IOException("Cannot write $uri")
                    val hashing = HashingSink.sha256(output.sink())
                    hashing.buffer().use { sink ->
                        val before = received
                        TransferStream.copyExactly(
                            data.source,
                            sink,
                            file.size,
                        ) { n -> progress(id, before + n, report = true) }
                    }
                    if (file.sha256.size > 0 &&
                        hashing.hash != file.sha256
                    ) {
                        throw IOException("SHA-256 mismatch for ${file.name}")
                    }
                    publish(uri)
                    received += file.size
                }
                messenger.send(
                    Envelope.Body.TransferProgress(
                        TransferProgress(
                            transfer_id = id,
                            bytes_done = received,
                            state = TransferState.TRANSFER_STATE_COMPLETED,
                        ),
                    ),
                )
                outcome = TransferOutcome.COMPLETED
            } catch (e: CancellationException) {
                outcome = TransferOutcome.CANCELLED
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "Receiving $id failed: ${e.message}")
                messenger.send(
                    Envelope.Body.TransferProgress(
                        TransferProgress(transfer_id = id, state = TransferState.TRANSFER_STATE_FAILED),
                    ),
                )
            } finally {
                data?.close()
                withContext(NonCancellable) {
                    if (outcome !=
                        TransferOutcome.COMPLETED
                    ) {
                        written.forEach { runCatching { context.contentResolver.delete(it, null, null) } }
                    }
                    finish(
                        TransferRecord(
                            id,
                            first,
                            files.size,
                            incoming = true,
                            Instant.now(),
                            offer.total_bytes,
                            outcome,
                            uri = written.firstOrNull()?.takeIf { outcome == TransferOutcome.COMPLETED }?.toString(),
                        ),
                    )
                }
            }
        }

        private fun createDownload(
            name: String,
            mimeType: String,
        ): Uri? {
            val values =
                ContentValues().apply {
                    put(MediaStore.Downloads.DISPLAY_NAME, name)
                    put(MediaStore.Downloads.MIME_TYPE, mimeType.ifEmpty { "application/octet-stream" })
                    put(MediaStore.Downloads.RELATIVE_PATH, "${Environment.DIRECTORY_DOWNLOADS}/$FOLDER")
                    put(MediaStore.Downloads.IS_PENDING, 1)
                }
            return context.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
        }

        private fun publish(uri: Uri) {
            context.contentResolver.update(
                uri,
                ContentValues().apply { put(MediaStore.Downloads.IS_PENDING, 0) },
                null,
                null,
            )
        }

        private fun hasRoomFor(bytes: Long): Boolean {
            val stat = StatFs(Environment.getExternalStorageDirectory().path)
            return stat.availableBytes - bytes > FREE_SPACE_MARGIN
        }

        private var lastReport = 0L

        private fun progress(
            id: String,
            done: Long,
            report: Boolean = false,
        ) {
            _active.update { list -> list.map { if (it.id == id) it.copy(bytesDone = done) else it } }
            val now = System.currentTimeMillis()
            if (now - lastReport < PROGRESS_INTERVAL_MS) return
            lastReport = now
            _active.value.firstOrNull { it.id == id }?.let { notifications.progress(it, cancelIntentFor(it.id)) }
            if (report) {
                scope.launch {
                    messenger.send(
                        Envelope.Body.TransferProgress(
                            TransferProgress(
                                transfer_id = id,
                                bytes_done = done,
                                state = TransferState.TRANSFER_STATE_IN_PROGRESS,
                            ),
                        ),
                    )
                }
            }
        }

        private fun onProgress(progress: TransferProgress) {
            when (progress.state) {
                TransferState.TRANSFER_STATE_COMPLETED,
                TransferState.TRANSFER_STATE_FAILED,
                TransferState.TRANSFER_STATE_CANCELLED,
                -> outcomes[progress.transfer_id]?.complete(progress.state)

                else -> Unit
            }
        }

        private fun track(transfer: ActiveTransfer) {
            _active.update { it + transfer }
            notifications.progress(transfer, cancelIntentFor(transfer.id))
        }

        private suspend fun finish(record: TransferRecord) {
            jobs.remove(record.id)
            _active.update { list -> list.filterNot { it.id == record.id } }
            notifications.finished(record)
            history.add(record)
            Log.i(TAG, "${if (record.incoming) "Received" else "Sent"} ${record.fileCount} file(s): ${record.outcome}")
        }

        private companion object {
            const val TAG = "FenpoTransfer"
            const val FOLDER = "Fenpo"
            const val OUTGOING_FOLDER = "outgoing"
            const val PROGRESS_INTERVAL_MS = 500L
            const val FREE_SPACE_MARGIN = 200L * 1024 * 1024
            val ACCEPT_TIMEOUT = 120.seconds
            val CONFIRM_TIMEOUT = 30.seconds
            val CONNECT_TIMEOUT = 30.seconds
            val READ_TIMEOUT = 30.seconds
        }
    }

/** File names from the other side, made safe for MediaStore. */
internal object FileNames {
    private const val MAX = 120

    /** Drops any path, control characters and characters Android file systems reject. */
    fun safe(name: String): String {
        val base = name.substringAfterLast('/').substringAfterLast('\\')
        val cleaned = base.filter { it >= ' ' && it !in "\"*:<>?|" }.trim().trimStart('.')
        return cleaned.ifEmpty { "file" }.take(MAX)
    }
}
