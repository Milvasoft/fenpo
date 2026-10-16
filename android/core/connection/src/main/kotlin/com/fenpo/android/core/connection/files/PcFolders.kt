package com.fenpo.android.core.connection.files

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.util.Log
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.transfer.SendResult
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.FileChange
import com.fenpo.protocol.v1.FileChangeKind
import com.fenpo.protocol.v1.FileFetch
import com.fenpo.protocol.v1.FileThumbnailRequest
import com.fenpo.protocol.v1.FolderEntry
import com.fenpo.protocol.v1.FolderListRequest
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import java.io.File
import java.io.IOException
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.time.Duration.Companion.minutes

/** Why a request about the PC's folders did not work. */
class PcFolderException(
    val offline: Boolean,
    message: String,
) : IOException(message)

/**
 * The PC's shared folders (files.proto), for [PcDocumentsProvider]: listings, previews and changes
 * are requests on the Wi-Fi channel; file contents come as a transfer the phone asked for and
 * uploads go as a transfer into a shared folder. Remembers the entries it listed, so the provider
 * can describe a document without asking again, and tells the system when a folder changed.
 */
@Singleton
class PcFolders
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val transferManager: TransferManager,
    ) : PcFeature {
        private val entries = ConcurrentHashMap<String, FolderEntry>()

        /** `true` while the PC shares folders and Wi-Fi is open. */
        val available: Boolean
            get() = Capability.CAPABILITY_FILE_PROVIDER in messenger.capabilities.value && messenger.wifiOpen

        override suspend fun run(): Unit =
            coroutineScope {
                // The root appears and goes with the connection.
                launch {
                    messenger.capabilities
                        .map { Capability.CAPABILITY_FILE_PROVIDER in it }
                        .distinctUntilChanged()
                        .collect { notify(DocumentsContract.buildRootsUri(PcDocumentsProvider.authority(context))) }
                }
                messenger.incoming.collect { envelope ->
                    val changed = (envelope.body as? Envelope.Body.FolderChanged)?.value ?: return@collect
                    notify(PcDocumentsProvider.childrenUri(context, changed.path))
                }
            }

        /** What is in [path] ("" for the shared folders). */
        suspend fun list(path: String): List<FolderEntry> {
            val answer = request(Envelope.Body.FolderListRequest(FolderListRequest(path = path)))
            val listing = (answer.body as? Envelope.Body.FolderListing)?.value ?: throw failure(answer)
            listing.entries.forEach { entries[it.path] = it }
            return listing.entries
        }

        /** The entry for [path] as last listed, listing its folder when not known yet. */
        suspend fun entry(path: String): FolderEntry? {
            entries[path]?.let { return it }
            list(PcPaths.parent(path))
            return entries[path]
        }

        /** A JPEG preview of [path], or `null` when the PC has none. */
        suspend fun thumbnail(
            path: String,
            px: Int,
        ): ByteArray? {
            val answer = request(Envelope.Body.FileThumbnailRequest(FileThumbnailRequest(path = path, max_px = px)))
            val jpeg = (answer.body as? Envelope.Body.FileThumbnail)?.value?.jpeg ?: throw failure(answer)
            return jpeg.toByteArray().takeIf { it.isNotEmpty() }
        }

        /** Downloads [path] into [target]. */
        suspend fun fetch(
            path: String,
            target: File,
        ) {
            if (!available) throw PcFolderException(offline = true, "Not connected on Wi-Fi")
            val id = UUID.randomUUID().toString()
            val done = transferManager.expect(id, target)
            if (!messenger.send(
                    Envelope.Body.FileFetch(FileFetch(path = path, transfer_id = id)),
                    Capability.CAPABILITY_FILE_PROVIDER,
                )
            ) {
                done.cancel()
                throw PcFolderException(offline = true, "FileFetch not sent")
            }
            val ok = withTimeoutOrNull(FETCH_TIMEOUT) { done.await() }
            if (ok != true) {
                done.cancel()
                throw PcFolderException(offline = false, "$path did not arrive")
            }
        }

        /** Sends [file] into the shared folder [folder]; a name already there gets " (2)" on the PC. */
        suspend fun upload(
            file: File,
            folder: String,
        ) {
            val result = transferManager.send(listOf(Uri.fromFile(file)), destinationPath = folder)
            Log.i(TAG, "Upload into a PC folder: $result")
            if (result != SendResult.STARTED) throw PcFolderException(offline = true, "Upload not started: $result")
        }

        /** Deletes, renames or creates a folder; returns the renamed or created entry. */
        suspend fun change(
            kind: FileChangeKind,
            path: String,
            newName: String = "",
        ): FolderEntry? {
            val answer = request(Envelope.Body.FileChange(FileChange(kind = kind, path = path, new_name = newName)))
            val result = (answer.body as? Envelope.Body.FileChangeResult)?.value ?: throw failure(answer)
            if (!result.ok) throw PcFolderException(offline = false, "The PC did not $kind $path")
            if (kind == FileChangeKind.FILE_CHANGE_KIND_DELETE ||
                kind == FileChangeKind.FILE_CHANGE_KIND_RENAME
            ) {
                entries.remove(path)
            }
            notify(
                PcDocumentsProvider.childrenUri(
                    context,
                    if (kind ==
                        FileChangeKind.FILE_CHANGE_KIND_CREATE_FOLDER
                    ) {
                        path
                    } else {
                        PcPaths.parent(path)
                    },
                ),
            )
            return result.entry?.also { entries[it.path] = it }
        }

        private suspend fun request(body: Envelope.Body): Envelope {
            if (!available) throw PcFolderException(offline = true, "Not connected on Wi-Fi")
            return messenger.request(body, Capability.CAPABILITY_FILE_PROVIDER)
                ?: throw PcFolderException(offline = false, "No answer for ${body.javaClass.simpleName}")
        }

        private fun failure(answer: Envelope): PcFolderException {
            val error = (answer.body as? Envelope.Body.Error)?.value
            return PcFolderException(
                offline = false,
                error?.message ?: "Unexpected ${answer.body?.javaClass?.simpleName}",
            )
        }

        private fun notify(uri: Uri) {
            context.contentResolver.notifyChange(uri, null)
        }

        private companion object {
            const val TAG = "FenpoPcFolders"
            val FETCH_TIMEOUT = 30.minutes
        }
    }

/** Paths in the PC's shared folders: relative, "/" between parts, "" for the shared folders. */
object PcPaths {
    fun parent(path: String): String = path.substringBeforeLast('/', missingDelimiterValue = "")

    fun child(
        folder: String,
        name: String,
    ): String = if (folder.isEmpty()) name else "$folder/$name"

    fun name(path: String): String = path.substringAfterLast('/')

    /** `true` when [path] is inside [folder], at any depth. */
    fun isInside(
        path: String,
        folder: String,
    ): Boolean = path != folder && (folder.isEmpty() || path.startsWith("$folder/"))
}

@Module
@InstallIn(SingletonComponent::class)
internal abstract class PcFoldersModule {
    @Binds
    @IntoSet
    abstract fun feature(folders: PcFolders): PcFeature
}
