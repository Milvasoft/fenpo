package com.fenpo.android.core.connection.photos

import android.content.ContentUris
import android.content.Context
import android.database.Cursor
import android.graphics.Bitmap
import android.net.Uri
import android.provider.MediaStore
import android.util.Log
import android.util.Size
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.transfer.SendResult
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.android.core.data.permissions.PhotoLibraryAccess
import com.fenpo.android.core.data.permissions.PhotoPermissions
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Error
import com.fenpo.protocol.v1.ErrorCode
import com.fenpo.protocol.v1.PhotoAccess
import com.fenpo.protocol.v1.PhotoFetch
import com.fenpo.protocol.v1.PhotoItem
import com.fenpo.protocol.v1.PhotoList
import com.fenpo.protocol.v1.PhotoListRequest
import com.fenpo.protocol.v1.PhotoThumbnail
import com.fenpo.protocol.v1.PhotoThumbnailRequest
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okio.ByteString.Companion.toByteString
import java.io.ByteArrayOutputStream
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

/**
 * The phone's photo library browsed from the PC (photos.proto): pages of photos and videos from
 * MediaStore, newest first, thumbnails for the ones on the PC's screen, and the originals as a file
 * transfer with the PC's id. Lists and thumbnails go only over Wi-Fi; on Bluetooth the PC is told
 * BUSY. Off with Settings > Features; without the photo permission the PC is told access is denied.
 */
@Singleton
class PhotoLibrary
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
        private val settingsRepository: SettingsRepository,
        private val transferManager: TransferManager,
    ) : PcFeature {
        // Thumbnails take a moment each; the PC's next request should not wait behind them.
        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

        override suspend fun run() {
            messenger.incoming.collect { envelope ->
                val body = envelope.body
                val isPhotos =
                    body is Envelope.Body.PhotoListRequest ||
                        body is Envelope.Body.PhotoThumbnailRequest ||
                        body is Envelope.Body.PhotoFetch
                if (!isPhotos) return@collect
                if (!settingsRepository.settings
                        .first()
                        .features.photoLibrary
                ) {
                    reply(envelope.id, error(ErrorCode.ERROR_CODE_UNSUPPORTED, "Photos are turned off on the phone"))
                    return@collect
                }
                if (!messenger.wifiOpen && body !is Envelope.Body.PhotoFetch) {
                    reply(envelope.id, error(ErrorCode.ERROR_CODE_BUSY, "Photos need Wi-Fi"))
                    return@collect
                }
                scope.launch {
                    when (body) {
                        is Envelope.Body.PhotoListRequest -> {
                            reply(
                                envelope.id,
                                Envelope.Body.PhotoList(list(body.value)),
                            )
                        }

                        is Envelope.Body.PhotoThumbnailRequest -> {
                            thumbnails(envelope.id, body.value)
                        }

                        is Envelope.Body.PhotoFetch -> {
                            fetch(envelope.id, body.value)
                        }

                        else -> {
                            Unit
                        }
                    }
                }
            }
        }

        private suspend fun reply(
            replyTo: Long,
            body: Envelope.Body,
        ) {
            messenger.send(body, Capability.CAPABILITY_PHOTOS, replyTo)
        }

        private fun error(
            code: ErrorCode,
            message: String,
        ) = Envelope.Body.Error(Error(code = code, message = message))

        private fun list(request: PhotoListRequest): PhotoList {
            val access = PhotoPermissions.access(context)
            if (access == PhotoLibraryAccess.DENIED) return PhotoList(access = PhotoAccess.PHOTO_ACCESS_DENIED)
            val limit =
                request.limit
                    .toInt()
                    .takeIf { it > 0 }
                    ?.coerceAtMost(MAX_PAGE) ?: MAX_PAGE
            val before = request.before?.toEpochMilli()
            var selection =
                "${MediaStore.Files.FileColumns.MEDIA_TYPE} IN (${MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE}, " +
                    "${MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO}) AND ${MediaStore.MediaColumns.IS_PENDING} = 0"
            val args = mutableListOf<String>()
            if (before != null) {
                selection += " AND ${MediaStore.MediaColumns.DATE_TAKEN} < ?"
                args += before.toString()
            }
            val items = mutableListOf<PhotoItem>()
            var more = false
            context.contentResolver
                .query(
                    MediaStore.Files.getContentUri(MediaStore.VOLUME_EXTERNAL),
                    COLUMNS,
                    selection,
                    args.toTypedArray(),
                    "${MediaStore.MediaColumns.DATE_TAKEN} DESC",
                )?.use { cursor ->
                    while (cursor.moveToNext()) {
                        if (items.size == limit) {
                            more = true
                            break
                        }
                        items += item(cursor)
                    }
                }
            return PhotoList(
                items = items,
                has_more = more,
                access =
                    if (access ==
                        PhotoLibraryAccess.LIMITED
                    ) {
                        PhotoAccess.PHOTO_ACCESS_LIMITED
                    } else {
                        PhotoAccess.PHOTO_ACCESS_FULL
                    },
            )
        }

        private fun item(cursor: Cursor): PhotoItem {
            val video = cursor.getInt(TYPE) == MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO
            // Screenshots and some downloads have no date taken; the date added stands in.
            val taken = cursor.getLong(TAKEN).takeIf { it > 0 } ?: (cursor.getLong(ADDED) * MILLIS)
            return PhotoItem(
                id = cursor.getLong(ID).toString(),
                taken_at = Instant.ofEpochMilli(taken),
                width = cursor.getInt(WIDTH),
                height = cursor.getInt(HEIGHT),
                is_video = video,
                duration_ms = if (video) cursor.getLong(DURATION).toInt() else 0,
                name = cursor.getString(NAME).orEmpty(),
                size = cursor.getLong(SIZE),
            )
        }

        private suspend fun thumbnails(
            replyTo: Long,
            request: PhotoThumbnailRequest,
        ) {
            val px =
                request.max_px
                    .toInt()
                    .takeIf { it > 0 }
                    ?.coerceAtMost(MAX_THUMBNAIL) ?: MAX_THUMBNAIL
            for (id in request.ids.take(MAX_THUMBNAILS)) {
                val jpeg = withContext(Dispatchers.IO) { thumbnail(id, px) }
                reply(replyTo, Envelope.Body.PhotoThumbnail(PhotoThumbnail(id = id, jpeg = jpeg.toByteString())))
            }
        }

        private fun thumbnail(
            id: String,
            px: Int,
        ): ByteArray {
            val uri = uriFor(id) ?: return ByteArray(0)
            return runCatching {
                val bitmap = context.contentResolver.loadThumbnail(uri, Size(px, px), null)
                ByteArrayOutputStream().use { out ->
                    bitmap.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, out)
                    bitmap.recycle()
                    out.toByteArray()
                }
            }.getOrElse { ByteArray(0) }
        }

        private suspend fun fetch(
            replyTo: Long,
            request: PhotoFetch,
        ) {
            val uris = request.ids.mapNotNull(::uriFor)
            val result = transferManager.send(uris, transferId = request.transfer_id)
            Log.i(TAG, "The PC asked for ${request.ids.size} photo(s): $result")
            if (result != SendResult.STARTED) {
                val code =
                    if (result ==
                        SendResult.NEEDS_WIFI
                    ) {
                        ErrorCode.ERROR_CODE_BUSY
                    } else {
                        ErrorCode.ERROR_CODE_INVALID_REQUEST
                    }
                reply(replyTo, error(code, "Photos not sent: $result"))
            }
        }

        /** The MediaStore item for an id the phone gave out, whether photo or video. */
        private fun uriFor(id: String): Uri? {
            val number = id.toLongOrNull() ?: return null
            return context.contentResolver
                .query(
                    MediaStore.Files.getContentUri(MediaStore.VOLUME_EXTERNAL),
                    arrayOf(MediaStore.Files.FileColumns.MEDIA_TYPE),
                    "${MediaStore.MediaColumns._ID} = ?",
                    arrayOf(id),
                    null,
                )?.use { cursor ->
                    if (!cursor.moveToFirst()) return null
                    val base =
                        if (cursor.getInt(0) == MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO) {
                            MediaStore.Video.Media.EXTERNAL_CONTENT_URI
                        } else {
                            MediaStore.Images.Media.EXTERNAL_CONTENT_URI
                        }
                    ContentUris.withAppendedId(base, number)
                }
        }

        private companion object {
            const val TAG = "FenpoPhotos"
            const val MAX_PAGE = 200
            const val MAX_THUMBNAILS = 60
            const val MAX_THUMBNAIL = 512
            const val JPEG_QUALITY = 80
            const val MILLIS = 1000L

            val COLUMNS =
                arrayOf(
                    MediaStore.MediaColumns._ID,
                    MediaStore.Files.FileColumns.MEDIA_TYPE,
                    MediaStore.MediaColumns.DATE_TAKEN,
                    MediaStore.MediaColumns.DATE_ADDED,
                    MediaStore.MediaColumns.WIDTH,
                    MediaStore.MediaColumns.HEIGHT,
                    MediaStore.MediaColumns.DURATION,
                    MediaStore.MediaColumns.DISPLAY_NAME,
                    MediaStore.MediaColumns.SIZE,
                )
            const val ID = 0
            const val TYPE = 1
            const val TAKEN = 2
            const val ADDED = 3
            const val WIDTH = 4
            const val HEIGHT = 5
            const val DURATION = 6
            const val NAME = 7
            const val SIZE = 8
        }
    }

@Module
@InstallIn(SingletonComponent::class)
internal abstract class PhotoLibraryModule {
    @Binds
    @IntoSet
    abstract fun feature(library: PhotoLibrary): PcFeature
}
