package com.fenpo.android.core.data.device

import android.content.Context
import android.os.Build
import android.provider.Settings
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
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
    ) : LocalDeviceRepository {
        override suspend fun localDevice(): LocalDevice =
            LocalDevice(
                id = deviceId(),
                name = deviceName(),
                appVersion = appVersion(),
                capabilities = IMPLEMENTED_CAPABILITIES,
                protocolVersions = ProtocolVersion.supported,
                osVersion = "Android ${Build.VERSION.RELEASE}",
                model = Build.MODEL,
            )

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
                )
        }
    }
