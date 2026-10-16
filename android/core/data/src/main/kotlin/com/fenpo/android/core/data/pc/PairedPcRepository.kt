package com.fenpo.android.core.data.pc

import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.longPreferencesKey
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
 * @property bleAddress Bluetooth address of the PC from the companion device association.
 * @property associationId CompanionDeviceManager association id, 0 before association.
 */
data class PairedPc(
    val id: String,
    val name: String,
    val certificateSha256: String = "",
    val addresses: List<String> = emptyList(),
    val bleAddress: String = "",
    val associationId: Int = 0,
)

/** AES-GCM counters of the BLE channel (docs/ANDROID.md §4); they never go back for one key. */
data class BleCounters(
    val nextSend: Long = 1,
    val lastReceived: Long = 0,
)

/** The paired PC, if any. Pairing writes it; home, PC and the connection read it. */
interface PairedPcRepository {
    /** The paired PC, or `null` before the first pairing. */
    val pairedPc: Flow<PairedPc?>

    /** Remembers [pc] as the paired PC, replacing any earlier one, with its BLE channel key. Counters restart. */
    suspend fun save(
        pc: PairedPc,
        bleChannelKey: ByteArray,
    )

    /** Stores the result of the companion device association. */
    suspend fun saveBleLink(
        associationId: Int,
        address: String,
    )

    /** Replaces the PC's data channel addresses ("ip:port"), best first, as last seen on the network. */
    suspend fun saveAddresses(addresses: List<String>)

    /** Current BLE channel counters. */
    suspend fun bleCounters(): BleCounters

    /** Stores the next send counter before a message leaves the phone. */
    suspend fun saveBleSendCounter(nextSend: Long)

    /** Stores the last counter accepted from the PC. */
    suspend fun saveBleReceivedCounter(lastReceived: Long)

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
                    bleAddress = prefs[PC_BLE_ADDRESS].orEmpty(),
                    associationId = prefs[PC_ASSOCIATION] ?: 0,
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
                it[PC_BLE_KEY] = Base64.encodeToString(sealed, Base64.NO_WRAP)
                it.remove(PC_BLE_ADDRESS)
                it.remove(PC_ASSOCIATION)
                it[SEND_COUNTER] = BleCounters().nextSend
                it[RECEIVED_COUNTER] = BleCounters().lastReceived
            }
        }

        override suspend fun saveBleLink(
            associationId: Int,
            address: String,
        ) {
            dataStore.edit {
                it[PC_ASSOCIATION] = associationId
                it[PC_BLE_ADDRESS] = address
            }
        }

        override suspend fun saveAddresses(addresses: List<String>) {
            dataStore.edit { it[PC_ADDRESSES] = addresses.joinToString(SEPARATOR) }
        }

        override suspend fun bleCounters(): BleCounters {
            val prefs = dataStore.data.first()
            val defaults = BleCounters()
            return BleCounters(
                prefs[SEND_COUNTER] ?: defaults.nextSend,
                prefs[RECEIVED_COUNTER] ?: defaults.lastReceived,
            )
        }

        override suspend fun saveBleSendCounter(nextSend: Long) {
            dataStore.edit { it[SEND_COUNTER] = maxOf(nextSend, it[SEND_COUNTER] ?: 0) }
        }

        override suspend fun saveBleReceivedCounter(lastReceived: Long) {
            dataStore.edit { it[RECEIVED_COUNTER] = maxOf(lastReceived, it[RECEIVED_COUNTER] ?: 0) }
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
            val PC_BLE_KEY = stringPreferencesKey("paired_pc_ble_channel_key_sealed")
            val PC_BLE_ADDRESS = stringPreferencesKey("paired_pc_ble_address")
            val PC_ASSOCIATION = intPreferencesKey("paired_pc_association_id")
            val SEND_COUNTER = longPreferencesKey("ble_next_send_counter")
            val RECEIVED_COUNTER = longPreferencesKey("ble_last_received_counter")
            val KEYS =
                listOf(
                    PC_ID,
                    PC_NAME,
                    PC_CERT,
                    PC_ADDRESSES,
                    PC_BLE_KEY,
                    PC_BLE_ADDRESS,
                    PC_ASSOCIATION,
                    SEND_COUNTER,
                    RECEIVED_COUNTER,
                )
        }
    }
