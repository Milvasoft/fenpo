package com.fenpo.android.feature.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.data.hotspot.HotspotRepository
import com.fenpo.android.core.data.notifications.NotificationAppsRepository
import com.fenpo.android.core.data.permissions.PermissionChecker
import com.fenpo.android.core.data.settings.AppLanguage
import com.fenpo.android.core.data.settings.Feature
import com.fenpo.android.core.data.settings.FeatureSettings
import com.fenpo.android.core.data.settings.LanguageController
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.data.settings.ThemeMode
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

/** Everything the Settings screen shows. */
data class SettingsUiState(
    val themeMode: ThemeMode = ThemeMode.SYSTEM,
    val language: AppLanguage = AppLanguage.SYSTEM,
    val features: FeatureSettings = FeatureSettings(),
    val missingPermissions: Int = 0,
    val mirroredApps: Int? = null,
    val hotspotName: String? = null,
)

@HiltViewModel
class SettingsViewModel
    @Inject
    constructor(
        private val settingsRepository: SettingsRepository,
        private val languageController: LanguageController,
        private val permissionChecker: PermissionChecker,
        notificationApps: NotificationAppsRepository,
        hotspot: HotspotRepository,
    ) : ViewModel() {
        private val systemState = MutableStateFlow(readSystemState())

        val uiState: StateFlow<SettingsUiState> =
            combine(
                settingsRepository.settings,
                systemState,
                notificationApps.settings,
                hotspot.details,
            ) { settings, system, apps, hotspotDetails ->
                SettingsUiState(
                    themeMode = settings.themeMode,
                    language = system.first,
                    features = settings.features,
                    missingPermissions = system.second,
                    mirroredApps = apps.apps.count { it.enabled }.takeIf { apps.apps.isNotEmpty() },
                    hotspotName = hotspotDetails?.ssid,
                )
            }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), SettingsUiState())

        /** Re-reads the language and permissions, which can change in system Settings. */
        fun refresh() {
            systemState.value = readSystemState()
        }

        fun setThemeMode(mode: ThemeMode) {
            viewModelScope.launch { settingsRepository.setThemeMode(mode) }
        }

        fun setLanguage(language: AppLanguage) {
            languageController.apply(language)
            refresh()
        }

        fun setFeatureEnabled(
            feature: Feature,
            enabled: Boolean,
        ) {
            viewModelScope.launch { settingsRepository.setFeatureEnabled(feature, enabled) }
        }

        private fun readSystemState() = languageController.current() to permissionChecker.missing().size
    }
