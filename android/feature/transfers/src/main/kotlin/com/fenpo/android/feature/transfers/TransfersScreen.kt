package com.fenpo.android.feature.transfers

import android.content.Intent
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.core.net.toUri
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.connection.transfer.ActiveTransfer
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.android.core.data.transfer.TransferRecord
import com.fenpo.android.core.designsystem.component.FenpoCard
import com.fenpo.android.core.designsystem.component.FenpoListRow
import com.fenpo.android.core.designsystem.component.FenpoRootTopBar
import com.fenpo.android.core.designsystem.component.FileTypeBadge
import com.fenpo.android.core.designsystem.component.RowDivider
import com.fenpo.android.core.designsystem.component.SectionLabel
import com.fenpo.android.core.designsystem.format.relativeTime
import java.time.Instant
import java.util.Locale

/** Transfers tab with its view model. */
@Composable
fun TransfersRoute(
    modifier: Modifier = Modifier,
    viewModel: TransfersViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val context = LocalContext.current
    TransfersScreen(
        state = state,
        modifier = modifier,
        onCancel = viewModel::cancel,
        onOpen = { record ->
            record.uri?.let { uri ->
                val intent =
                    Intent(Intent.ACTION_VIEW, uri.toUri()).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                runCatching { context.startActivity(intent) }
            }
        },
    )
}

/**
 * Transfers tab. The design does not detail this screen; it uses the rows of "Recent transfers" on
 * the home screen: transfers in progress with their bar and "Cancel", then the history.
 */
@Composable
fun TransfersScreen(
    modifier: Modifier = Modifier,
    state: TransfersUiState = TransfersUiState(),
    onCancel: (String) -> Unit = {},
    onOpen: (TransferRecord) -> Unit = {},
) {
    Column(modifier = modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        FenpoRootTopBar(title = stringResource(R.string.transfers_title))
        if (state.active.isEmpty() && state.history.isEmpty()) {
            EmptyCard()
            return@Column
        }
        if (state.active.isNotEmpty()) {
            SectionLabel(stringResource(R.string.transfers_now))
            FenpoCard(modifier = Modifier.padding(horizontal = 16.dp)) {
                state.active.forEachIndexed { index, transfer ->
                    if (index > 0) RowDivider()
                    ActiveRow(transfer, onCancel)
                }
            }
        }
        if (state.history.isNotEmpty()) {
            SectionLabel(stringResource(R.string.transfers_history))
            FenpoCard(modifier = Modifier.padding(horizontal = 16.dp)) {
                state.history.forEachIndexed { index, record ->
                    if (index > 0) RowDivider()
                    HistoryRow(record, state.now, onOpen)
                }
            }
        }
        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun EmptyCard() {
    FenpoCard(modifier = Modifier.padding(horizontal = 16.dp).fillMaxWidth()) {
        Column(modifier = Modifier.padding(20.dp)) {
            Text(stringResource(R.string.transfers_empty_title), style = MaterialTheme.typography.titleMedium)
            Text(
                text = stringResource(R.string.transfers_empty_body),
                modifier = Modifier.padding(top = 8.dp),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun ActiveRow(
    transfer: ActiveTransfer,
    onCancel: (String) -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        FenpoListRow(
            title = transfer.fileName,
            supporting =
                withCount(
                    stringResource(if (transfer.incoming) R.string.transfers_receiving else R.string.transfers_sending),
                    transfer.fileCount,
                ),
            leading = { Badge(transfer.fileName) },
            trailing = {
                TextButton(onClick = { onCancel(transfer.id) }) { Text(stringResource(R.string.transfers_cancel)) }
            },
        )
        val fraction = if (transfer.totalBytes > 0) transfer.bytesDone.toFloat() / transfer.totalBytes else 0f
        LinearProgressIndicator(
            progress = { fraction },
            modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 24.dp, bottom = 12.dp),
        )
    }
}

@Composable
private fun HistoryRow(
    record: TransferRecord,
    now: Instant,
    onOpen: (TransferRecord) -> Unit,
) {
    val time = relativeTime(record.at, now)
    val supporting =
        when (record.outcome) {
            TransferOutcome.COMPLETED -> {
                stringResource(if (record.incoming) R.string.transfers_from_pc else R.string.transfers_to_pc, time)
            }

            TransferOutcome.DECLINED -> {
                stringResource(R.string.transfers_declined, time)
            }

            TransferOutcome.CANCELLED -> {
                stringResource(R.string.transfers_cancelled, time)
            }

            TransferOutcome.FAILED -> {
                stringResource(R.string.transfers_failed, time)
            }
        }
    FenpoListRow(
        title = record.fileName,
        supporting = withCount(supporting, record.fileCount),
        leading = { Badge(record.fileName) },
        modifier = if (record.uri != null) Modifier.clickable { onOpen(record) } else Modifier,
    )
}

/** [status], plus the file count when there are several: "Sent to the PC · 12 files". */
@Composable
private fun withCount(
    status: String,
    count: Int,
): String =
    if (count == 1) {
        status
    } else {
        stringResource(
            R.string.transfers_with_count,
            status,
            pluralStringResource(R.plurals.transfers_count, count, count),
        )
    }

@Composable
private fun Badge(fileName: String) {
    val extension =
        fileName
            .substringAfterLast('.', "")
            .uppercase(Locale.ROOT)
            .take(4)
    val isImage = extension in setOf("JPG", "JPEG", "PNG", "HEIC", "WEBP", "GIF")
    val colors = MaterialTheme.colorScheme
    FileTypeBadge(
        label = extension,
        containerColor = if (isImage) colors.tertiaryContainer else colors.primaryContainer,
        contentColor = if (isImage) colors.onTertiaryContainer else colors.onPrimaryContainer,
    )
}
