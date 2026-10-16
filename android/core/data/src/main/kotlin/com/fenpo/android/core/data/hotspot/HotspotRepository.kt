package com.fenpo.android.core.data.hotspot

import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import com.fenpo.android.core.security.SecretBox
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.withContext
import javax.inject.Inject
import javax.inject.Singleton

/** The phone's hotspot name and password as the user typed them (hotspot.proto). */
data class HotspotDetails(
    val ssid: String,
    val passphrase: String,
)

/**
 * The hotspot details the user typed in Settings > Hotspot. Android lets no app read them or turn
 * the hotspot on, so the user types them once and the phone hands them to the PC.
 */
interface HotspotRepository {
    /** The saved details, or `null` when none are saved. */
    val details: Flow<HotspotDetails?>

    suspend fun save(details: HotspotDetails)

    suspend fun clear()
}

/** [HotspotRepository] in Preferences DataStore, sealed with a Keystore key ([SecretBox]). */
@Singleton
internal class DataStoreHotspotRepository
    @Inject
    constructor(
        private val dataStore: DataStore<Preferences>,
        private val secretBox: SecretBox,
    ) : HotspotRepository {
        override val details: Flow<HotspotDetails?> =
            dataStore.data.map { prefs ->
                val ssid = prefs[SSID]?.let(::open) ?: return@map null
                val passphrase = prefs[PASSPHRASE]?.let(::open) ?: return@map null
                HotspotDetails(ssid, passphrase)
            }

        override suspend fun save(details: HotspotDetails) {
            val ssid = withContext(Dispatchers.IO) { seal(details.ssid) }
            val passphrase = withContext(Dispatchers.IO) { seal(details.passphrase) }
            dataStore.edit {
                it[SSID] = ssid
                it[PASSPHRASE] = passphrase
            }
        }

        override suspend fun clear() {
            dataStore.edit {
                it.remove(SSID)
                it.remove(PASSPHRASE)
            }
        }

        private fun seal(text: String): String =
            Base64.encodeToString(secretBox.seal(text.toByteArray()), Base64.NO_WRAP)

        /** `null` when the Keystore key is gone (the phone was restored): the user types them again. */
        private fun open(sealed: String): String? =
            runCatching { String(secretBox.open(Base64.decode(sealed, Base64.NO_WRAP))) }.getOrNull()

        private companion object {
            val SSID = stringPreferencesKey("hotspot_ssid")
            val PASSPHRASE = stringPreferencesKey("hotspot_passphrase")
        }
    }
