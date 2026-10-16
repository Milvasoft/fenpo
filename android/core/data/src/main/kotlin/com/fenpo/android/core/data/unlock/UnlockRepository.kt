package com.fenpo.android.core.data.unlock

import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import javax.inject.Inject
import javax.inject.Singleton

/** The PC this phone can unlock, and its key sealed by a biometric Keystore key (unlock.proto). */
data class UnlockEnrollment(
    val pcId: String,
    val pcName: String,
    val iv: ByteArray,
    val sealedKey: ByteArray,
) {
    override fun equals(other: Any?): Boolean =
        other is UnlockEnrollment && other.pcId == pcId && other.pcName == pcName &&
            other.iv.contentEquals(iv) && other.sealedKey.contentEquals(sealedKey)

    override fun hashCode(): Int = pcId.hashCode()
}

/**
 * The paired PC's unlock key as stored on the phone: only sealed, so reading it here gives nothing
 * without the fingerprint. One PC at a time, like pairing.
 */
interface UnlockRepository {
    val enrollment: Flow<UnlockEnrollment?>

    suspend fun save(enrollment: UnlockEnrollment)

    suspend fun clear()
}

@Singleton
internal class DataStoreUnlockRepository
    @Inject
    constructor(
        private val dataStore: DataStore<Preferences>,
    ) : UnlockRepository {
        override val enrollment: Flow<UnlockEnrollment?> =
            dataStore.data.map { prefs ->
                val id = prefs[PC_ID] ?: return@map null
                UnlockEnrollment(
                    pcId = id,
                    pcName = prefs[PC_NAME].orEmpty(),
                    iv = Base64.decode(prefs[IV].orEmpty(), Base64.NO_WRAP),
                    sealedKey = Base64.decode(prefs[SEALED].orEmpty(), Base64.NO_WRAP),
                )
            }

        override suspend fun save(enrollment: UnlockEnrollment) {
            dataStore.edit {
                it[PC_ID] = enrollment.pcId
                it[PC_NAME] = enrollment.pcName
                it[IV] = Base64.encodeToString(enrollment.iv, Base64.NO_WRAP)
                it[SEALED] = Base64.encodeToString(enrollment.sealedKey, Base64.NO_WRAP)
            }
        }

        override suspend fun clear() {
            if (enrollment.first() == null) return
            dataStore.edit {
                it.remove(PC_ID)
                it.remove(PC_NAME)
                it.remove(IV)
                it.remove(SEALED)
            }
        }

        private companion object {
            val PC_ID = stringPreferencesKey("unlock_pc_id")
            val PC_NAME = stringPreferencesKey("unlock_pc_name")
            val IV = stringPreferencesKey("unlock_iv")
            val SEALED = stringPreferencesKey("unlock_sealed_key")
        }
    }
