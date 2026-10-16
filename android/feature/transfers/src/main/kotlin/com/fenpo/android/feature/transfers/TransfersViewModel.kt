package com.fenpo.android.feature.transfers

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.connection.transfer.ActiveTransfer
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.android.core.data.transfer.TransferHistory
import com.fenpo.android.core.data.transfer.TransferRecord
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import java.time.Instant
import javax.inject.Inject

/**
 * What the Transfers tab shows.
 *
 * @property now Reference time for relative times; injected so screenshots are stable.
 */
data class TransfersUiState(
    val active: List<ActiveTransfer> = emptyList(),
    val history: List<TransferRecord> = emptyList(),
    val now: Instant = Instant.EPOCH,
)

@HiltViewModel
class TransfersViewModel
    @Inject
    constructor(
        private val transferManager: TransferManager,
        transferHistory: TransferHistory,
    ) : ViewModel() {
        val uiState: StateFlow<TransfersUiState> =
            combine(transferManager.active, transferHistory.records) { active, history ->
                TransfersUiState(active, history, Instant.now())
            }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), TransfersUiState())

        fun cancel(id: String) = transferManager.cancel(id)
    }
