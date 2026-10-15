package com.fenpo.android.core.data.settings

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.emptyPreferences
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.test.runTest
import org.junit.Test
import kotlin.test.assertEquals

class DataStoreSettingsRepositoryTest {
    /** In-memory store; the file storage itself is DataStore's concern, not this repository's. */
    private class InMemoryPreferences : DataStore<Preferences> {
        private val state = MutableStateFlow(emptyPreferences())
        override val data: Flow<Preferences> = state

        override suspend fun updateData(transform: suspend (t: Preferences) -> Preferences): Preferences =
            transform(state.value).also { state.value = it }
    }

    @Test
    fun startsWithDefaults() =
        runTest {
            assertEquals(UserSettings(), DataStoreSettingsRepository(InMemoryPreferences()).settings.first())
        }

    @Test
    fun storesThemeAndFeatures() =
        runTest {
            val repository = DataStoreSettingsRepository(InMemoryPreferences())

            repository.setThemeMode(ThemeMode.DARK)
            repository.setFeatureEnabled(Feature.SCREENSHOT_SHARING, true)
            repository.setFeatureEnabled(Feature.MESSAGE_SYNC, false)

            val settings = repository.settings.first()
            assertEquals(ThemeMode.DARK, settings.themeMode)
            assertEquals(FeatureSettings(screenshotSharing = true, messageSync = false), settings.features)
        }
}
