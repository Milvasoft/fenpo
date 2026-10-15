package com.fenpo.android.feature.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.data.pc.PairedPcRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import java.time.Instant
import javax.inject.Inject

/**
 * State for the home and PC screens. Until the connection layer exists (phase C) the PC is shown
 * as paired but not connected, and transfers, clipboard and mirroring stay empty.
 */
@HiltViewModel
class HomeViewModel
    @Inject
    constructor(
        pairedPcRepository: PairedPcRepository,
    ) : ViewModel() {
        val uiState: StateFlow<HomeUiState> =
            pairedPcRepository.pairedPc
                .map { pc ->
                    HomeUiState(
                        pc =
                            pc?.let {
                                PcCardState(
                                    name = it.name,
                                    osLabel = null,
                                    connected = false,
                                    bluetooth = false,
                                    wifi = false,
                                )
                            },
                        now = Instant.now(),
                    )
                }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), HomeUiState())
    }
