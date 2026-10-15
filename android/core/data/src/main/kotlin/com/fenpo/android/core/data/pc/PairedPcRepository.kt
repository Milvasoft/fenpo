package com.fenpo.android.core.data.pc

import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import com.fenpo.android.core.security.SecretBox
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.withContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * A PC this phone has paired with.
 *
 * @property id Stable PC id from the pairing code.
 * @property name User visible name, e.g. "BUGRA-PC".
 * @property certificateSha256 Hex SHA-256 of the PC's TLS certificate; the pin for every connection.
 * @property addresses Last known data channel endpoints, `host:port` or `[ipv6]:port`.
 * @property bleServiceUuid Fenpo GATT service UUID this PC advertises.
 */
data class PairedPc(
    val id: String,
    val name: String,
    val certificateSha256: String = "",
    val addresses: List<String> = emptyList(),
    val bleServiceUuid: String = "",
)

/** The paired PC, if any. Pairing writes it; home, PC and the connection read it. */
interface PairedPcRepository {
    /** The paired PC, or `null` before the first pairing. */
    val pairedPc: Flow<PairedPc?>

    /** Remembers [pc] as the paired PC, replacing any earlier one, with its BLE channel key. */
    suspend fun save(
        pc: PairedPc,
        bleChannelKey: ByteArray,
    )

    /** The BLE channel key of the paired PC, decrypted, or `null` if none. */
    suspend fun bleChannelKey(): ByteArray?

    /** Forgets the paired PC. */
    suspend fun clear()
}

/**
 * [PairedPcRepository] backed by Preferences DataStore. The BLE channel key is sealed with a
 * Keystore key ([SecretBox]); the TLS key itself never leaves the Keystore.
 */
@Singleton
internal class DataStorePairedPcRepository
    @Inject
    constructor(
        private val dataStore: DataStore<Preferences>,
        private val secretBox: SecretBox,
    ) : PairedPcRepository {
        override val pairedPc: Flow<PairedPc?> =
            dataStore.data.map { prefs ->
                val id = prefs[PC_ID] ?: return@map null
                PairedPc(
                    id = id,
                    name = prefs[PC_NAME].orEmpty(),
                    certificateSha256 = prefs[PC_CERT].orEmpty(),
                    addresses = prefs[PC_ADDRESSES]?.split(SEPARATOR)?.filter { it.isNotEmpty() }.orEmpty(),
                    bleServiceUuid = prefs[PC_BLE_SERVICE].orEmpty(),
                )
            }

        override suspend fun save(
            pc: PairedPc,
            bleChannelKey: ByteArray,
        ) {
            val sealed = withContext(Dispatchers.IO) { secretBox.seal(bleChannelKey) }
            dataStore.edit {
                it[PC_ID] = pc.id
                it[PC_NAME] = pc.name
                it[PC_CERT] = pc.certificateSha256
                it[PC_ADDRESSES] = pc.addresses.joinToString(SEPARATOR)
                it[PC_BLE_SERVICE] = pc.bleServiceUuid
                it[PC_BLE_KEY] = Base64.encodeToString(sealed, Base64.NO_WRAP)
            }
        }

        override suspend fun bleChannelKey(): ByteArray? {
            val stored = dataStore.data.first()[PC_BLE_KEY] ?: return null
            return withContext(Dispatchers.IO) { secretBox.open(Base64.decode(stored, Base64.NO_WRAP)) }
        }

        override suspend fun clear() {
            dataStore.edit { prefs -> KEYS.forEach { prefs.remove(it) } }
        }

        private companion object {
            const val SEPARATOR = ","
            val PC_ID = stringPreferencesKey("paired_pc_id")
            val PC_NAME = stringPreferencesKey("paired_pc_name")
            val PC_CERT = stringPreferencesKey("paired_pc_certificate_sha256")
            val PC_ADDRESSES = stringPreferencesKey("paired_pc_addresses")
            val PC_BLE_SERVICE = stringPreferencesKey("paired_pc_ble_service_uuid")
            val PC_BLE_KEY = stringPreferencesKey("paired_pc_ble_channel_key_sealed")
            val KEYS = listOf(PC_ID, PC_NAME, PC_CERT, PC_ADDRESSES, PC_BLE_SERVICE, PC_BLE_KEY)
        }
    }
