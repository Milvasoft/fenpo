package com.fenpo.android.feature.transfers.screenshots

import android.Manifest
import android.content.ContentUris
import android.content.Context
import android.content.pm.PackageManager
import android.database.ContentObserver
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.MediaStore
import android.util.Log
import androidx.core.content.ContextCompat
import com.fenpo.android.core.connection.PcFeature
import com.fenpo.android.core.connection.transfer.SendResult
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.android.core.data.settings.SettingsRepository
import dagger.Binds
import dagger.Module
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import dagger.multibindings.IntoSet
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import javax.inject.Inject
import javax.inject.Singleton

/**
 * "Send screenshots" (Settings > Features, off by default): every new screenshot goes to the PC as
 * a file transfer while the PC is connected. Android tells apps about new images through
 * MediaStore; reading another app's image needs the photo permission, asked for when the user
 * turns the switch on.
 */
@Singleton
class ScreenshotSender
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val settingsRepository: SettingsRepository,
        private val transferManager: TransferManager,
    ) : PcFeature {
        private var lastSentId = -1L

        override suspend fun run() {
            settingsRepository.settings
                .map { it.features.screenshotSharing }
                .distinctUntilChanged()
                .collectLatest { on -> if (on && canRead(context)) watch() }
        }

        private suspend fun watch() {
            val changes = Channel<Unit>(Channel.CONFLATED)
            val observer =
                object : ContentObserver(Handler(Looper.getMainLooper())) {
                    override fun onChange(selfChange: Boolean) {
                        changes.trySend(Unit)
                    }
                }
            context.contentResolver.registerContentObserver(
                MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
                true,
                observer,
            )
            lastSentId = newestScreenshot()?.first ?: -1L
            try {
                for (change in changes) {
                    val (id, uri) = newestScreenshot() ?: continue
                    if (id <= lastSentId) continue
                    lastSentId = id
                    val result = transferManager.send(listOf(uri))
                    Log.i(TAG, "New screenshot: $result")
                    if (result != SendResult.STARTED) Log.d(TAG, "Screenshot not sent: $result")
                }
            } finally {
                context.contentResolver.unregisterContentObserver(observer)
            }
        }

        /** The newest finished image in a Screenshots folder, taken in the last minute. */
        private fun newestScreenshot(): Pair<Long, Uri>? {
            val since = System.currentTimeMillis() / MILLIS - RECENT_SECONDS
            val selection =
                "${MediaStore.Images.Media.RELATIVE_PATH} LIKE ? AND ${MediaStore.Images.Media.IS_PENDING} = 0 " +
                    "AND ${MediaStore.Images.Media.DATE_ADDED} >= ?"
            val args = arrayOf("%Screenshots%", since.toString())
            context.contentResolver
                .query(
                    MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
                    arrayOf(MediaStore.Images.Media._ID),
                    selection,
                    args,
                    "${MediaStore.Images.Media._ID} DESC",
                )?.use { cursor ->
                    if (!cursor.moveToFirst()) return null
                    val id = cursor.getLong(0)
                    return id to ContentUris.withAppendedId(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, id)
                }
            return null
        }

        companion object {
            private const val TAG = "FenpoScreenshots"
            private const val MILLIS = 1000
            private const val RECENT_SECONDS = 60
            private const val ANDROID_13 = 33

            /** The permission that lets Fenpo read new screenshots on this Android version. */
            val permission: String
                get() =
                    if (Build.VERSION.SDK_INT >=
                        ANDROID_13
                    ) {
                        Manifest.permission.READ_MEDIA_IMAGES
                    } else {
                        Manifest.permission.READ_EXTERNAL_STORAGE
                    }

            fun canRead(context: Context): Boolean =
                ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED
        }
    }

@Module
@InstallIn(SingletonComponent::class)
internal abstract class ScreenshotModule {
    @Binds
    @IntoSet
    abstract fun feature(sender: ScreenshotSender): PcFeature
}
