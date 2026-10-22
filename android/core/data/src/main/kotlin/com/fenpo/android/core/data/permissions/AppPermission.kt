package com.fenpo.android.core.data.permissions

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.PowerManager
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/** The permissions listed on the Permissions screen, in display order. */
enum class AppPermission {
    /** Bluetooth scan and connect; on Android 17 also the local network (same system group). */
    NEARBY_DEVICES,

    /** Fenpo's own notifications: the connection, transfers, clipboard from the PC. */
    POST_NOTIFICATIONS,

    /** Notification listener access; only granted in system Settings. */
    NOTIFICATION_ACCESS,

    /** Battery optimization exemption, so the connection survives the whole day. */
    BATTERY,

    /** SMS for phones whose Bluetooth stack does not offer messages (docs/ANDROID.md §5). */
    SMS,

    /** QR code scanning and the webcam. */
    CAMERA,
    ;

    /** Runtime permissions to request for this item; empty when it is granted in system Settings instead. */
    val runtimePermissions: List<String>
        get() =
            when (this) {
                NEARBY_DEVICES -> {
                    buildList {
                        add(Manifest.permission.BLUETOOTH_SCAN)
                        add(Manifest.permission.BLUETOOTH_CONNECT)
                        if (Build.VERSION.SDK_INT >= ANDROID_17) add(ACCESS_LOCAL_NETWORK)
                    }
                }

                POST_NOTIFICATIONS -> {
                    // A runtime permission from Android 13; granted with the install before that.
                    if (Build.VERSION.SDK_INT >=
                        ANDROID_13
                    ) {
                        listOf(Manifest.permission.POST_NOTIFICATIONS)
                    } else {
                        emptyList()
                    }
                }

                NOTIFICATION_ACCESS, BATTERY -> {
                    emptyList()
                }

                SMS -> {
                    listOf(Manifest.permission.READ_SMS, Manifest.permission.RECEIVE_SMS, Manifest.permission.SEND_SMS)
                }

                CAMERA -> {
                    listOf(Manifest.permission.CAMERA)
                }
            }

    companion object {
        /** Permissions the Permissions screen lists. SMS is not declared in this release (no SMS permissions in the manifest). */
        val shown: List<AppPermission> = entries.filterNot { it == SMS }

        private const val ANDROID_13 = 33
        private const val ANDROID_17 = 37

        // Manifest.permission.ACCESS_LOCAL_NETWORK, API 37 (docs/ANDROID.md §3).
        private const val ACCESS_LOCAL_NETWORK = "android.permission.ACCESS_LOCAL_NETWORK"
    }
}

/** Checks which [AppPermission]s are granted. Cheap; call again when the app resumes. */
interface PermissionChecker {
    /** Whether [permission] is fully granted. */
    fun isGranted(permission: AppPermission): Boolean

    /** Permissions that are not granted yet, in display order. */
    fun missing(): List<AppPermission> = AppPermission.shown.filterNot(::isGranted)
}

/** [PermissionChecker] that asks the system. */
@Singleton
internal class SystemPermissionChecker
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
    ) : PermissionChecker {
        override fun isGranted(permission: AppPermission): Boolean =
            when (permission) {
                AppPermission.NOTIFICATION_ACCESS -> {
                    context.packageName in NotificationManagerCompat.getEnabledListenerPackages(context)
                }

                AppPermission.BATTERY -> {
                    context
                        .getSystemService(
                            PowerManager::class.java,
                        ).isIgnoringBatteryOptimizations(context.packageName)
                }

                else -> {
                    permission.runtimePermissions.all {
                        ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED
                    }
                }
            }
    }
