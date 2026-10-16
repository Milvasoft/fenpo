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
         * SMS over the Fenpo channel only when the user wants message sync and allowed SMS; with
         * MAP on the phone the PC reads messages over Bluetooth instead (docs/ANDROID.md §7).
         */
        private suspend fun optionalCapabilities(): Set<Capability> {
            val sms =
                listOf(Manifest.permission.READ_SMS, Manifest.permission.SEND_SMS).all {
                    ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED
                }
            val wanted =
                settingsRepository.settings
                    .first()
                    .features.messageSync
            return if (sms && wanted) setOf(Capability.CAPABILITY_MESSAGES) else emptySet()
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
                )
        }
    }
