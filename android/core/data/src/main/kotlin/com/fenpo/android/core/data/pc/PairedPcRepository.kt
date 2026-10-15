package com.fenpo.android.core.data.pc

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import javax.inject.Inject
import javax.inject.Singleton

/**
 * A PC this phone has paired with.
 *
 * @property id Stable PC id from the pairing code.
 * @property name User visible name, e.g. "BUGRA-PC".
 */
data class PairedPc(
    val id: String,
    val name: String,
)

/** The paired PC, if any. Pairing (phase C) writes it; the home and PC screens read it. */
interface PairedPcRepository {
    /** The paired PC, or `null` before the first pairing. */
    val pairedPc: Flow<PairedPc?>

    /** Remembers [pc] as the paired PC. */
    suspend fun save(pc: PairedPc)

    /** Forgets the paired PC. */
    suspend fun clear()
}

/** [PairedPcRepository] backed by Preferences DataStore. Certificates live in the Android Keystore, not here. */
@Singleton
internal class DataStorePairedPcRepository
    @Inject
    constructor(
        private val dataStore: DataStore<Preferences>,
    ) : PairedPcRepository {
        override val pairedPc: Flow<PairedPc?> =
            dataStore.data.map { prefs ->
                val id = prefs[PC_ID] ?: return@map null
                PairedPc(id = id, name = prefs[PC_NAME].orEmpty())
            }

        override suspend fun save(pc: PairedPc) {
            dataStore.edit {
                it[PC_ID] = pc.id
                it[PC_NAME] = pc.name
            }
        }

        override suspend fun clear() {
            dataStore.edit {
                it.remove(PC_ID)
                it.remove(PC_NAME)
            }
        }

        private companion object {
            val PC_ID = stringPreferencesKey("paired_pc_id")
            val PC_NAME = stringPreferencesKey("paired_pc_name")
        }
    }
