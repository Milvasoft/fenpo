package com.fenpo.android

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.data.entitlements.InstallRecord
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.data.settings.ThemeMode
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import javax.inject.Inject

/** App-wide UI state: the appearance setting. `null` until settings are loaded. */
@HiltViewModel
class MainViewModel
    @Inject
    constructor(
        settingsRepository: SettingsRepository,
        installRecord: InstallRecord,
    ) : ViewModel() {
        init {
            // The first version a user ran decides what they keep if features become paid later.
            viewModelScope.launch { installRecord.recordFirstRun() }
        }

        val themeMode: StateFlow<ThemeMode?> =
            settingsRepository.settings
                .map { it.themeMode }
                .stateIn(viewModelScope, SharingStarted.Eagerly, null)
    }
