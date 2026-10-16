package com.fenpo.android.core.connection.files

import android.content.Context
import android.content.res.AssetFileDescriptor
import android.database.Cursor
import android.database.MatrixCursor
import android.graphics.Point
import android.net.Uri
import android.os.Bundle
import android.os.CancellationSignal
import android.os.Handler
import android.os.Looper
import android.os.ParcelFileDescriptor
import android.provider.DocumentsContract
import android.provider.DocumentsContract.Document
import android.provider.DocumentsContract.Root
import android.provider.DocumentsProvider
import android.util.Log
import android.webkit.MimeTypeMap
import com.fenpo.android.core.connection.R
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.protocol.v1.FileChangeKind
import com.fenpo.protocol.v1.FolderEntry
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import java.io.File
import java.io.FileNotFoundException
import java.security.MessageDigest
import java.util.UUID
import kotlin.coroutines.cancellation.CancellationException
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * The PC's shared folders in Android's file picker and Files app (files.proto, docs/ANDROID.md §9).
 * Document ids are "pc:" and the path in the shared folders. The system calls these methods on
 * background threads and expects them to block, so each waits for its request to the PC. Files
 * opened for reading are downloaded into the cache first; files written are uploaded into their
 * folder when the writing app closes them (the PC adds " (2)" to a name already there).
 */
class PcDocumentsProvider : DocumentsProvider() {
    @EntryPoint
    @InstallIn(SingletonComponent::class)
    interface Dependencies {
        fun folders(): PcFolders

        fun pairedPcRepository(): PairedPcRepository
    }

    private val dependencies by lazy {
        EntryPointAccessors.fromApplication(requireNotNull(context).applicationContext, Dependencies::class.java)
    }
    private val folders get() = dependencies.folders()
    private val uploads = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /** Documents created here and not written yet: the PC has no such file until the upload. */
    private val created = HashMap<String, String>()

    override fun onCreate(): Boolean = true

    override fun queryRoots(projection: Array<out String>?): Cursor {
        val cursor = MatrixCursor(projection ?: ROOT_COLUMNS)
        val context = requireNotNull(context)
        val pc = runBlocking { dependencies.pairedPcRepository().pairedPc.first() } ?: return cursor
        cursor.newRow().apply {
            add(Root.COLUMN_ROOT_ID, ROOT_ID)
            add(Root.COLUMN_DOCUMENT_ID, documentId(""))
            add(Root.COLUMN_TITLE, pc.name.ifEmpty { context.getString(R.string.files_root_title) })
            add(Root.COLUMN_SUMMARY, context.getString(R.string.files_root_summary))
            add(Root.COLUMN_ICON, DesignR.drawable.ic_pc)
            add(Root.COLUMN_FLAGS, Root.FLAG_SUPPORTS_CREATE or Root.FLAG_SUPPORTS_IS_CHILD)
        }
        return cursor
    }

    override fun queryDocument(
        documentId: String,
        projection: Array<out String>?,
    ): Cursor {
        val cursor = MatrixCursor(projection ?: DOCUMENT_COLUMNS)
        val path = pathOf(documentId)
        if (path.isEmpty()) {
            cursor.newRow().apply {
                add(Document.COLUMN_DOCUMENT_ID, documentId)
                add(Document.COLUMN_DISPLAY_NAME, requireNotNull(context).getString(R.string.files_root_title))
                add(Document.COLUMN_MIME_TYPE, Document.MIME_TYPE_DIR)
                add(Document.COLUMN_FLAGS, 0)
            }
            return cursor
        }
        synchronized(created) { created[path] }?.let { mime ->
            addRow(cursor, FolderEntry(name = PcPaths.name(path), path = path, mime_type = mime))
            return cursor
        }
        val entry = blocking(null) { folders.entry(path) } ?: throw FileNotFoundException(path)
        addRow(cursor, entry)
        return cursor
    }

    override fun queryChildDocuments(
        parentDocumentId: String,
        projection: Array<out String>?,
        sortOrder: String?,
    ): Cursor {
        val context = requireNotNull(context)
        val path = pathOf(parentDocumentId)
        val cursor = MatrixCursor(projection ?: DOCUMENT_COLUMNS)
        cursor.setNotificationUri(context.contentResolver, childrenUri(context, path))
        try {
            blocking(null) { folders.list(path) }.forEach { addRow(cursor, it) }
        } catch (e: PcFolderException) {
            Log.i(TAG, "Listing a PC folder failed: ${e.message}")
            val message = if (e.offline) R.string.files_error_offline else R.string.files_error_failed
            cursor.extras = Bundle().apply { putString(DocumentsContract.EXTRA_ERROR, context.getString(message)) }
        }
        return cursor
    }

    override fun isChildDocument(
        parentDocumentId: String,
        documentId: String,
    ): Boolean = PcPaths.isInside(pathOf(documentId), pathOf(parentDocumentId))

    override fun openDocument(
        documentId: String,
        mode: String,
        signal: CancellationSignal?,
    ): ParcelFileDescriptor {
        val path = pathOf(documentId)
        val access = ParcelFileDescriptor.parseMode(mode)
        if (mode.contains('w')) return openForUpload(path, access)
        val entry = blocking(signal) { folders.entry(path) } ?: throw FileNotFoundException(path)
        val file = File(File(requireNotNull(context).cacheDir, CACHE_FOLDER), cacheName(entry))
        if (!file.exists()) blocking(signal) { folders.fetch(path, file) }
        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
    }

    /** The writing app gets a cache file; when it closes it, the file goes into its PC folder. */
    private fun openForUpload(
        path: String,
        access: Int,
    ): ParcelFileDescriptor {
        val folder =
            File(File(requireNotNull(context).cacheDir, UPLOAD_FOLDER), UUID.randomUUID().toString()).apply {
                mkdirs()
            }
        val file = File(folder, PcPaths.name(path))
        return ParcelFileDescriptor.open(
            file,
            access or ParcelFileDescriptor.MODE_CREATE,
            Handler(Looper.getMainLooper()),
        ) { error ->
            synchronized(created) { created.remove(path) }
            if (error != null) {
                folder.deleteRecursively()
                return@open
            }
            uploads.launch {
                try {
                    folders.upload(file, PcPaths.parent(path))
                } catch (e: PcFolderException) {
                    Log.w(TAG, "Upload failed: ${e.message}")
                } finally {
                    // The transfer opened the file already; the copy can go.
                    folder.deleteRecursively()
                }
            }
        }
    }

    override fun openDocumentThumbnail(
        documentId: String,
        sizeHint: Point,
        signal: CancellationSignal?,
    ): AssetFileDescriptor {
        val path = pathOf(documentId)
        val px = maxOf(sizeHint.x, sizeHint.y).coerceIn(1, MAX_THUMBNAIL)
        val jpeg =
            blocking(signal) { folders.thumbnail(path, px) } ?: throw FileNotFoundException("No preview for $path")
        val file =
            File(
                File(requireNotNull(context).cacheDir, THUMBNAIL_FOLDER).apply { mkdirs() },
                digest(path) + ".jpg",
            )
        file.writeBytes(jpeg)
        val descriptor = ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
        return AssetFileDescriptor(descriptor, 0, AssetFileDescriptor.UNKNOWN_LENGTH)
    }

    override fun createDocument(
        parentDocumentId: String,
        mimeType: String,
        displayName: String,
    ): String {
        val parent = pathOf(parentDocumentId)
        if (mimeType == Document.MIME_TYPE_DIR) {
            val entry =
                blocking(null) { folders.change(FileChangeKind.FILE_CHANGE_KIND_CREATE_FOLDER, parent, displayName) }
                    ?: throw FileNotFoundException("Folder not created")
            return documentId(entry.path)
        }
        val path = PcPaths.child(parent, displayName)
        synchronized(created) { created[path] = mimeType }
        return documentId(path)
    }

    override fun deleteDocument(documentId: String) {
        blocking(null) { folders.change(FileChangeKind.FILE_CHANGE_KIND_DELETE, pathOf(documentId)) }
    }

    override fun renameDocument(
        documentId: String,
        displayName: String,
    ): String? {
        val entry =
            blocking(null) { folders.change(FileChangeKind.FILE_CHANGE_KIND_RENAME, pathOf(documentId), displayName) }
        return entry?.let { documentId(it.path) }
    }

    override fun getDocumentType(documentId: String): String {
        val path = pathOf(documentId)
        if (path.isEmpty()) return Document.MIME_TYPE_DIR
        synchronized(created) { created[path] }?.let { return it }
        return blocking(null) { folders.entry(path) }?.let(::mimeOf) ?: throw FileNotFoundException(path)
    }

    private fun addRow(
        cursor: MatrixCursor,
        entry: FolderEntry,
    ) {
        val topLevel = PcPaths.parent(entry.path).isEmpty()
        val mime = mimeOf(entry)
        var flags = 0
        if (!entry.read_only) {
            flags =
                if (entry.is_folder) {
                    Document.FLAG_DIR_SUPPORTS_CREATE
                } else {
                    Document.FLAG_SUPPORTS_WRITE
                }
            // The shared folders themselves are the PC user's choice; the phone does not remove them.
            if (!topLevel) flags = flags or Document.FLAG_SUPPORTS_DELETE or Document.FLAG_SUPPORTS_RENAME
        }
        if (!entry.is_folder &&
            THUMBNAIL_TYPES.any { mime.startsWith(it) }
        ) {
            flags = flags or Document.FLAG_SUPPORTS_THUMBNAIL
        }
        cursor.newRow().apply {
            add(Document.COLUMN_DOCUMENT_ID, documentId(entry.path))
            add(Document.COLUMN_DISPLAY_NAME, entry.name)
            add(Document.COLUMN_MIME_TYPE, mime)
            add(Document.COLUMN_SIZE, if (entry.is_folder) null else entry.size)
            add(Document.COLUMN_LAST_MODIFIED, entry.modified_at?.toEpochMilli())
            add(Document.COLUMN_FLAGS, flags)
        }
    }

    private fun mimeOf(entry: FolderEntry): String =
        when {
            entry.is_folder -> {
                Document.MIME_TYPE_DIR
            }

            entry.mime_type.isNotEmpty() -> {
                entry.mime_type
            }

            else -> {
                MimeTypeMap.getSingleton().getMimeTypeFromExtension(entry.name.substringAfterLast('.', "").lowercase())
                    ?: "application/octet-stream"
            }
        }

    /** One cached copy per path and version: a file changed on the PC is downloaded again. */
    private fun cacheName(entry: FolderEntry): String =
        digest("${entry.path}|${entry.size}|${entry.modified_at?.toEpochMilli()}") + "-" + entry.name

    /** Runs a request to the PC on this binder thread; the picker's cancel ends it. */
    private fun <T> blocking(
        signal: CancellationSignal?,
        block: suspend () -> T,
    ): T =
        try {
            runBlocking {
                val job = coroutineContext[kotlinx.coroutines.Job]
                signal?.setOnCancelListener { job?.cancel() }
                block()
            }
        } catch (e: CancellationException) {
            throw android.os.OperationCanceledException()
        } catch (e: PcFolderException) {
            throw FileNotFoundException(e.message)
        }

    companion object {
        private const val TAG = "FenpoPcFolders"
        private const val ROOT_ID = "pc"
        private const val PREFIX = "pc:"
        private const val CACHE_FOLDER = "pc_files"
        private const val UPLOAD_FOLDER = "pc_upload"
        private const val THUMBNAIL_FOLDER = "pc_thumbnails"
        private const val MAX_THUMBNAIL = 512
        private const val DIGEST_BYTES = 12
        private val THUMBNAIL_TYPES = listOf("image/", "video/", "application/pdf")

        private val ROOT_COLUMNS =
            arrayOf(
                Root.COLUMN_ROOT_ID,
                Root.COLUMN_DOCUMENT_ID,
                Root.COLUMN_TITLE,
                Root.COLUMN_SUMMARY,
                Root.COLUMN_ICON,
                Root.COLUMN_FLAGS,
            )
        private val DOCUMENT_COLUMNS =
            arrayOf(
                Document.COLUMN_DOCUMENT_ID,
                Document.COLUMN_DISPLAY_NAME,
                Document.COLUMN_MIME_TYPE,
                Document.COLUMN_SIZE,
                Document.COLUMN_LAST_MODIFIED,
                Document.COLUMN_FLAGS,
            )

        fun authority(context: Context): String = context.packageName + ".pcfolders"

        fun childrenUri(
            context: Context,
            path: String,
        ): Uri = DocumentsContract.buildChildDocumentsUri(authority(context), documentId(path))

        private fun documentId(path: String) = PREFIX + path

        private fun pathOf(documentId: String): String {
            if (!documentId.startsWith(PREFIX)) throw FileNotFoundException(documentId)
            return documentId.removePrefix(PREFIX)
        }

        private fun digest(text: String): String =
            MessageDigest
                .getInstance("SHA-256")
                .digest(text.toByteArray())
                .take(DIGEST_BYTES)
                .joinToString("") { "%02x".format(it) }
    }
}
