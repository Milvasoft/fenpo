package com.fenpo.android.core.connection

import com.fenpo.android.core.data.settings.Feature
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.data.settings.ThemeMode
import com.fenpo.android.core.data.settings.UserSettings
import kotlinx.coroutines.flow.MutableStateFlow

/** In-memory settings for tests; features default to on as in the app. */
class FakeSettings : SettingsRepository {
    private val state = MutableStateFlow(UserSettings())
    override val settings = state

    fun update(change: (UserSettings) -> UserSettings) {
        state.value = change(state.value)
    }

    override suspend fun setThemeMode(mode: ThemeMode) = update { it.copy(themeMode = mode) }

    override suspend fun setFeatureEnabled(
        feature: Feature,
        enabled: Boolean,
    ) = Unit
}
