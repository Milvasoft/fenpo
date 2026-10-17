package com.fenpo.android.core.data.entitlements

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

/** The app version and time of the first launch on this phone. */
data class FirstRun(
    val version: String,
    val at: Instant,
)

/**
 * Remembers the first version the user ran, like the Windows app's FirstVersion and FirstRunAt, so
 * people who had a feature before it became paid can keep it ([Entitlements]). Written once.
 */
interface InstallRecord {
    val firstRun: Flow<FirstRun?>

    /** Stores this launch as the first one, unless one is stored already. */
    suspend fun recordFirstRun()
}

@Singleton
internal class DataStoreInstallRecord
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val dataStore: DataStore<Preferences>,
    ) : InstallRecord {
        override val firstRun: Flow<FirstRun?> =
            dataStore.data.map { prefs ->
                val version = prefs[FIRST_VERSION] ?: return@map null
                FirstRun(version, Instant.ofEpochMilli(prefs[FIRST_RUN_AT] ?: 0L))
            }

        override suspend fun recordFirstRun() {
            val version =
                context.packageManager
                    .getPackageInfo(context.packageName, 0)
                    .versionName
                    .orEmpty()
            dataStore.edit {
                if (it[FIRST_VERSION] == null) {
                    it[FIRST_VERSION] = version
                    it[FIRST_RUN_AT] = System.currentTimeMillis()
                }
            }
        }

        private companion object {
            val FIRST_VERSION = stringPreferencesKey("first_version")
            val FIRST_RUN_AT = longPreferencesKey("first_run_at")
        }
    }
