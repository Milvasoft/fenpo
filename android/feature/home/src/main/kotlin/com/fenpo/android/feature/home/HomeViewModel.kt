package com.fenpo.android.feature.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.connection.ConnectionStatus
import com.fenpo.android.core.connection.LinkState
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.Hello
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import java.time.Instant
import javax.inject.Inject

/**
 * State for the home and PC screens: the paired PC and the live connection paths. Transfers,
 * clipboard and mirroring stay empty until those features exist.
 */
@HiltViewModel
class HomeViewModel
    @Inject
    constructor(
        pairedPcRepository: PairedPcRepository,
        connectionStatus: ConnectionStatus,
    ) : ViewModel() {
        val uiState: StateFlow<HomeUiState> =
            combine(pairedPcRepository.pairedPc, connectionStatus.snapshot) { pc, connection ->
                HomeUiState(
                    pc =
                        pc?.let {
                            PcCardState(
                                name = it.name,
                                osLabel = connection.remote?.osLabel(),
                                connected = connection.connected,
                                bluetooth = connection.bluetooth == LinkState.CONNECTED,
                                wifi = connection.wifi == LinkState.CONNECTED,
                            )
                        },
                    now = Instant.now(),
                )
            }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), HomeUiState())

        /** "Windows 11 26300" from Hello; just "Windows" from a PC that does not send the version yet. */
        private fun Hello.osLabel(): String? =
            os_version.ifBlank { if (platform == DevicePlatform.DEVICE_PLATFORM_WINDOWS) WINDOWS else null }

        private companion object {
            const val WINDOWS = "Windows"
        }
    }
