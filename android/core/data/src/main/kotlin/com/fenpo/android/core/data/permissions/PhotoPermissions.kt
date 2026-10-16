package com.fenpo.android.core.data.permissions

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat

/** How much of the photo library Fenpo may read. */
enum class PhotoLibraryAccess { FULL, LIMITED, DENIED }

/**
 * The photo and video permissions for "Photos on the PC" (photos.proto): READ_MEDIA_IMAGES and
 * READ_MEDIA_VIDEO from Android 13, READ_EXTERNAL_STORAGE before. From Android 14 the user may
 * allow only some photos (READ_MEDIA_VISUAL_USER_SELECTED), which the PC sees as limited access.
 */
object PhotoPermissions {
    /** What to ask for when the user turns the feature on. */
    val request: Array<String>
        get() =
            when {
                Build.VERSION.SDK_INT >= ANDROID_14 -> {
                    arrayOf(
                        Manifest.permission.READ_MEDIA_IMAGES,
                        Manifest.permission.READ_MEDIA_VIDEO,
                        Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED,
                    )
                }

                Build.VERSION.SDK_INT >= ANDROID_13 -> {
                    arrayOf(Manifest.permission.READ_MEDIA_IMAGES, Manifest.permission.READ_MEDIA_VIDEO)
                }

                else -> {
                    arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
                }
            }

    fun access(context: Context): PhotoLibraryAccess {
        fun granted(permission: String) =
            ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED
        return when {
            Build.VERSION.SDK_INT >= ANDROID_13 && granted(Manifest.permission.READ_MEDIA_IMAGES) -> {
                PhotoLibraryAccess.FULL
            }

            Build.VERSION.SDK_INT >= ANDROID_14 && granted(Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED) -> {
                PhotoLibraryAccess.LIMITED
            }

            Build.VERSION.SDK_INT < ANDROID_13 && granted(Manifest.permission.READ_EXTERNAL_STORAGE) -> {
                PhotoLibraryAccess.FULL
            }

            else -> {
                PhotoLibraryAccess.DENIED
            }
        }
    }

    private const val ANDROID_13 = 33
    private const val ANDROID_14 = 34
}
