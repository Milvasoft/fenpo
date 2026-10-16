package com.fenpo.android.core.connection.media

import android.content.ComponentName
import android.content.Context
import android.database.ContentObserver
import android.graphics.Bitmap
import android.media.AudioManager
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.provider.Settings
import android.util.Log
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.notifications.FenpoNotificationListener
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Error
import com.fenpo.protocol.v1.ErrorCode
import com.fenpo.protocol.v1.MediaAction
import com.fenpo.protocol.v1.MediaCommand
import com.fenpo.protocol.v1.MediaState
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import okio.ByteString
import okio.ByteString.Companion.toByteString
import java.io.ByteArrayOutputStream
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.math.roundToInt

/**
 * The phone's now playing for the PC and the PC's media commands (media.proto), the Android
 * counterpart of the iPhone's AMS. Media sessions are readable because Fenpo is a notification
 * listener; without that access the phone reports nothing playing.
 */
@Singleton
class MediaBridge
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val messenger: PcMessenger,
    ) {
        private val sessions = context.getSystemService(MediaSessionManager::class.java)
        private val audio = context.getSystemService(AudioManager::class.java)
        private val listener = ComponentName(context, FenpoNotificationListener::class.java)
        private val main = Handler(Looper.getMainLooper())

        @Volatile private var controller: MediaController? = null

        /** The current media state; a new value on every change of player, track, play state or volume. */
        fun states(): Flow<MediaState> =
            callbackFlow {
                var watched: MediaController? = null
                val callback =
                    object : MediaController.Callback() {
                        override fun onMetadataChanged(metadata: MediaMetadata?) {
                            trySend(read())
                        }

                        override fun onPlaybackStateChanged(state: PlaybackState?) {
                            trySend(read())
                        }

                        override fun onSessionDestroyed() {
                            trySend(read())
                        }
                    }

                fun follow(controllers: List<MediaController>?) {
                    val chosen = pick(controllers.orEmpty())
                    if (chosen?.sessionToken != watched?.sessionToken) {
                        watched?.unregisterCallback(callback)
                        chosen?.registerCallback(callback, main)
                        watched = chosen
                        controller = chosen
                    }
                    trySend(read())
                }
                val sessionsChanged = MediaSessionManager.OnActiveSessionsChangedListener { follow(it) }
                val volume =
                    object : ContentObserver(main) {
                        override fun onChange(selfChange: Boolean) {
                            trySend(read())
                        }
                    }
                try {
                    sessions.addOnActiveSessionsChangedListener(sessionsChanged, listener, main)
                    follow(sessions.getActiveSessions(listener))
                } catch (e: SecurityException) {
                    // No notification access: nothing to report.
                    Log.i(TAG, "Media sessions not readable: ${e.message}")
                    trySend(MediaState(active = false))
                }
                context.contentResolver.registerContentObserver(Settings.System.CONTENT_URI, true, volume)
                awaitClose {
                    runCatching { sessions.removeOnActiveSessionsChangedListener(sessionsChanged) }
                    watched?.unregisterCallback(callback)
                    context.contentResolver.unregisterContentObserver(volume)
                    controller = null
                }
                // Settings.System also changes for unrelated settings; only real differences go out.
            }.distinctUntilChanged { a, b -> a.copy(position_at = null) == b.copy(position_at = null) }

        /** Carries out the PC's `MediaCommand`; answers `UNSUPPORTED` when nothing can be controlled. */
        suspend fun run() {
            messenger.incoming.collect { envelope ->
                val command = (envelope.body as? Envelope.Body.MediaCommand)?.value ?: return@collect
                if (!apply(command)) {
                    messenger.send(
                        Envelope.Body.Error(
                            Error(code = ErrorCode.ERROR_CODE_UNSUPPORTED, message = "Nothing is playing"),
                        ),
                        replyTo = envelope.id,
                    )
                }
            }
        }

        private fun apply(command: MediaCommand): Boolean {
            if (command.action == MediaAction.MEDIA_ACTION_SET_VOLUME) {
                val max = audio.getStreamMaxVolume(AudioManager.STREAM_MUSIC)
                val level = (command.volume_percent.coerceIn(0, PERCENT) * max / PERCENT.toFloat()).roundToInt()
                audio.setStreamVolume(AudioManager.STREAM_MUSIC, level, 0)
                return true
            }
            val controls = controller?.transportControls ?: return false
            val playing = controller?.playbackState?.state == PlaybackState.STATE_PLAYING
            when (command.action) {
                MediaAction.MEDIA_ACTION_PLAY -> controls.play()
                MediaAction.MEDIA_ACTION_PAUSE -> controls.pause()
                MediaAction.MEDIA_ACTION_TOGGLE -> if (playing) controls.pause() else controls.play()
                MediaAction.MEDIA_ACTION_NEXT -> controls.skipToNext()
                MediaAction.MEDIA_ACTION_PREVIOUS -> controls.skipToPrevious()
                MediaAction.MEDIA_ACTION_SEEK -> controls.seekTo(command.seek_to_ms)
                else -> return false
            }
            return true
        }

        /** The session that is playing, else the first one Android lists (most recently active). */
        private fun pick(controllers: List<MediaController>): MediaController? =
            controllers.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PLAYING }
                ?: controllers.firstOrNull()

        private fun read(): MediaState {
            val current = controller
            val metadata = current?.metadata
            val playback = current?.playbackState
            if (current == null || metadata == null) return MediaState(active = false, volume_percent = volumePercent())
            val actions = playback?.actions ?: 0L
            return MediaState(
                active = true,
                app_id = current.packageName,
                app_name = appName(current.packageName),
                title = metadata.getString(MediaMetadata.METADATA_KEY_TITLE).orEmpty(),
                artist =
                    (
                        metadata.getString(MediaMetadata.METADATA_KEY_ARTIST)
                            ?: metadata.getString(MediaMetadata.METADATA_KEY_ALBUM_ARTIST)
                    ).orEmpty(),
                album = metadata.getString(MediaMetadata.METADATA_KEY_ALBUM).orEmpty(),
                playing = playback?.state == PlaybackState.STATE_PLAYING,
                duration_ms = metadata.getLong(MediaMetadata.METADATA_KEY_DURATION).coerceAtLeast(0),
                position_ms = playback?.position?.coerceAtLeast(0) ?: 0,
                position_at =
                    playback?.let {
                        Instant.ofEpochMilli(
                            System.currentTimeMillis() - SystemClock.elapsedRealtime() + it.lastPositionUpdateTime,
                        )
                    },
                volume_percent = volumePercent(),
                can_previous = actions and PlaybackState.ACTION_SKIP_TO_PREVIOUS != 0L,
                can_next = actions and PlaybackState.ACTION_SKIP_TO_NEXT != 0L,
                can_seek = actions and PlaybackState.ACTION_SEEK_TO != 0L,
                artwork_jpeg = artwork(metadata),
            )
        }

        private fun volumePercent(): Int {
            val max = audio.getStreamMaxVolume(AudioManager.STREAM_MUSIC).coerceAtLeast(1)
            return audio.getStreamVolume(AudioManager.STREAM_MUSIC) * PERCENT / max
        }

        private fun appName(appId: String): String =
            runCatching {
                context.packageManager.let { it.getApplicationLabel(it.getApplicationInfo(appId, 0)).toString() }
            }.getOrDefault(appId)

        private var artworkKey: String? = null
        private var artworkCache: ByteString = ByteString.EMPTY

        /** Compressed once per track. */
        private fun artwork(metadata: MediaMetadata): ByteString {
            val key =
                listOf(
                    MediaMetadata.METADATA_KEY_TITLE,
                    MediaMetadata.METADATA_KEY_ARTIST,
                    MediaMetadata.METADATA_KEY_ALBUM,
                ).joinToString("\u0000") { metadata.getString(it).orEmpty() }
            if (key == artworkKey) return artworkCache
            artworkCache = encodeArtwork(metadata)
            artworkKey = key
            return artworkCache
        }

        private fun encodeArtwork(metadata: MediaMetadata): ByteString {
            val bitmap =
                metadata.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART)
                    ?: metadata.getBitmap(MediaMetadata.METADATA_KEY_ART)
                    ?: metadata.getBitmap(MediaMetadata.METADATA_KEY_DISPLAY_ICON)
                    ?: return ByteString.EMPTY
            val scale = minOf(1f, ARTWORK_PX.toFloat() / maxOf(bitmap.width, bitmap.height))
            val scaled =
                if (scale < 1f) {
                    Bitmap.createScaledBitmap(
                        bitmap,
                        (bitmap.width * scale).toInt(),
                        (bitmap.height * scale).toInt(),
                        true,
                    )
                } else {
                    bitmap
                }
            val out = ByteArrayOutputStream()
            scaled.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, out)
            return out.toByteArray().toByteString()
        }

        private companion object {
            const val TAG = "FenpoMedia"
            const val PERCENT = 100
            const val ARTWORK_PX = 300
            const val JPEG_QUALITY = 80
        }
    }
