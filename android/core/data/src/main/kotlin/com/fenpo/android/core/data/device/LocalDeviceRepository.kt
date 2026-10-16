package com.fenpo.android.core.data.device

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.provider.Settings
import androidx.core.content.ContextCompat
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.protocol.ProtocolVersion
import com.fenpo.android.core.transport.session.LocalDevice
import com.fenpo.protocol.v1.Capability
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.first
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

/** This phone as other Fenpo devices see it in `Hello` and `PairingRequest`. */
interface LocalDeviceRepository {
    /** Device id, name, app version and the capabilities this build implements. */
    suspend fun localDevice(): LocalDevice
}

@Singleton
internal class AndroidLocalDeviceRepository
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val dataStore: DataStore<Preferences>,
        private val settingsRepository: SettingsRepository,
    ) : LocalDeviceRepository {
        override suspend fun localDevice(): LocalDevice =
            LocalDevice(
                id = deviceId(),
                name = deviceName(),
                appVersion = appVersion(),
                capabilities = IMPLEMENTED_CAPABILITIES + optionalCapabilities(),
                protocolVersions = ProtocolVersion.supported,
                osVersion = "Android ${Build.VERSION.RELEASE}",
                model = Build.MODEL,
            )

        /**
         * Features the user can turn off. SMS over the Fenpo channel only when the user wants
         * message sync and allowed SMS; with MAP on the phone the PC reads messages over Bluetooth
         * instead (docs/ANDROID.md §7). The photo library is offered without the permission too: the
         * PC is then told the access is denied and asks its user to allow it on the phone.
         */
        private suspend fun optionalCapabilities(): Set<Capability> {
            val sms =
                listOf(Manifest.permission.READ_SMS, Manifest.permission.SEND_SMS).all {
                    ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED
                }
            val features = settingsRepository.settings.first().features
            return buildSet {
                if (sms && features.messageSync) add(Capability.CAPABILITY_MESSAGES)
                if (features.pcNotifications) add(Capability.CAPABILITY_PC_NOTIFICATIONS)
                if (features.photoLibrary) add(Capability.CAPABILITY_PHOTOS)
            }
        }

        /** Stable UUID created on first use. */
        private suspend fun deviceId(): String {
            dataStore.data.first()[DEVICE_ID]?.let { return it }
            val created = UUID.randomUUID().toString()
            var stored = created
            dataStore.edit { prefs -> stored = prefs[DEVICE_ID] ?: created.also { prefs[DEVICE_ID] = it } }
            return stored
        }

        /** The name the user gave the phone in Settings > About, else the model. */
        private fun deviceName(): String =
            Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() }
                ?: Build.MODEL

        private fun appVersion(): String =
            context.packageManager
                .getPackageInfo(context.packageName, 0)
                .versionName
                .orEmpty()

        private companion object {
            val DEVICE_ID = stringPreferencesKey("device_id")

            /** Only what this build really does; grows as features land. */
            val IMPLEMENTED_CAPABILITIES =
                setOf(
                    Capability.CAPABILITY_DEVICE_STATE,
                    Capability.CAPABILITY_CLIPBOARD,
                    Capability.CAPABILITY_OPEN_URL,
                    Capability.CAPABILITY_RING,
                    Capability.CAPABILITY_LOCK_PC,
                    Capability.CAPABILITY_FILE_TRANSFER,
                    Capability.CAPABILITY_NOTIFICATION_MIRROR,
                    Capability.CAPABILITY_MEDIA,
                    // Asked for when the PC starts them: a tap (camera) or Android's consent (screen).
                    Capability.CAPABILITY_CAMERA,
                    Capability.CAPABILITY_SCREEN_MIRROR,
                    Capability.CAPABILITY_PC_INPUT,
                    Capability.CAPABILITY_FILE_PROVIDER,
                    // Answered NOT_SET_UP until the user types the hotspot's name and password.
                    Capability.CAPABILITY_HOTSPOT,
                )
        }
    }
