package com.fenpo.android.core.data.settings

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import javax.inject.Inject
import javax.inject.Singleton

/** Reads and writes [UserSettings]. */
interface SettingsRepository {
    /** Current settings; emits again on every change. */
    val settings: Flow<UserSettings>

    /** Stores the appearance choice. */
    suspend fun setThemeMode(mode: ThemeMode)

    /** Turns a feature on or off. */
    suspend fun setFeatureEnabled(
        feature: Feature,
        enabled: Boolean,
    )
}

/** [SettingsRepository] backed by Preferences DataStore. */
@Singleton
internal class DataStoreSettingsRepository
    @Inject
    constructor(
        private val dataStore: DataStore<Preferences>,
    ) : SettingsRepository {
        override val settings: Flow<UserSettings> =
            dataStore.data.map { prefs ->
                val defaults = FeatureSettings()
                UserSettings(
                    themeMode =
                        prefs[Keys.THEME_MODE]
                            ?.let { stored -> ThemeMode.entries.firstOrNull { it.name == stored } }
                            ?: ThemeMode.SYSTEM,
                    features =
                        FeatureSettings(
                            notificationMirroring =
                                prefs[Keys.feature(Feature.NOTIFICATION_MIRRORING)] ?: defaults.notificationMirroring,
                            messageSync = prefs[Keys.feature(Feature.MESSAGE_SYNC)] ?: defaults.messageSync,
                            clipboardSharing =
                                prefs[Keys.feature(Feature.CLIPBOARD_SHARING)] ?: defaults.clipboardSharing,
                            screenshotSharing =
                                prefs[Keys.feature(Feature.SCREENSHOT_SHARING)] ?: defaults.screenshotSharing,
                            doNotDisturbSync =
                                prefs[Keys.feature(Feature.DO_NOT_DISTURB_SYNC)] ?: defaults.doNotDisturbSync,
                        ),
                )
            }

        override suspend fun setThemeMode(mode: ThemeMode) {
            dataStore.edit { it[Keys.THEME_MODE] = mode.name }
        }

        override suspend fun setFeatureEnabled(
            feature: Feature,
            enabled: Boolean,
        ) {
            dataStore.edit { it[Keys.feature(feature)] = enabled }
        }

        private object Keys {
            val THEME_MODE = stringPreferencesKey("theme_mode")

            fun feature(feature: Feature) = booleanPreferencesKey("feature_" + feature.name.lowercase())
        }
    }
