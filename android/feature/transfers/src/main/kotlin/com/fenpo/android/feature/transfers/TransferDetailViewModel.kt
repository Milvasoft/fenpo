package com.fenpo.android.feature.transfers

import android.content.Context
import android.net.Uri
import android.provider.MediaStore
import androidx.core.net.toUri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.navigation3.runtime.NavKey
import com.fenpo.android.core.connection.transfer.ActiveTransfer
import com.fenpo.android.core.connection.transfer.SendResult
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.android.core.data.transfer.TransferHistory
import com.fenpo.android.core.data.transfer.TransferRecord
import dagger.assisted.Assisted
import dagger.assisted.AssistedFactory
import dagger.assisted.AssistedInject
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import java.time.Instant

/** Navigation key of one transfer's details. */
@Serializable
data class TransferDetailKey(
    val id: String,
) : NavKey

/**
 * What the transfer screen shows: the transfer while it moves, its record once it ended.
 *
 * @property fileExists For a received transfer, whether its first file is still on the phone.
 */
data class TransferDetailUiState(
    val active: ActiveTransfer? = null,
    val record: TransferRecord? = null,
    val fileExists: Boolean = false,
    val now: Instant = Instant.EPOCH,
)

@HiltViewModel(assistedFactory = TransferDetailViewModel.Factory::class)
class TransferDetailViewModel
    @AssistedInject
    constructor(
        @Assisted private val id: String,
        @param:ApplicationContext private val context: Context,
        private val transferManager: TransferManager,
        transferHistory: TransferHistory,
    ) : ViewModel() {
        val uiState: StateFlow<TransferDetailUiState> =
            combine(transferManager.active, transferHistory.records) { active, records ->
                val record = records.firstOrNull { it.id == id }
                TransferDetailUiState(
                    active = active.firstOrNull { it.id == id },
                    record = record,
                    fileExists = record?.uri?.let { exists(it.toUri()) } ?: false,
                    now = Instant.now(),
                )
            }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), TransferDetailUiState())

        fun cancel() = transferManager.cancel(id)

        /** "Choose and send again": Fenpo keeps no copy of what it sends, so the user picks the files again. */
        suspend fun send(uris: List<Uri>): SendResult = transferManager.send(uris)

        private suspend fun exists(uri: Uri): Boolean =
            withContext(Dispatchers.IO) {
                runCatching {
                    context.contentResolver.query(uri, arrayOf(MediaStore.MediaColumns._ID), null, null, null)?.use {
                        it.count >
                            0
                    }
                }.getOrNull() == true
            }

        @AssistedFactory
        interface Factory {
            fun create(id: String): TransferDetailViewModel
        }
    }
