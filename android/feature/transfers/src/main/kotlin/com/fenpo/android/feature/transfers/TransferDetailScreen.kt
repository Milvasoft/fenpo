package com.fenpo.android.feature.transfers

import android.content.Intent
import android.text.format.Formatter
import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.core.net.toUri
import androidx.core.os.ConfigurationCompat
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.connection.transfer.SendResult
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.android.core.designsystem.component.FenpoCard
import com.fenpo.android.core.designsystem.component.FenpoListRow
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.component.NoteBox
import com.fenpo.android.core.designsystem.component.RowDivider
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import com.fenpo.android.core.connection.R as ConnectionR

/** One transfer's details with its view model. */
@Composable
fun TransferDetailRoute(
    id: String,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val viewModel = hiltViewModel<TransferDetailViewModel, TransferDetailViewModel.Factory>(key = id) { it.create(id) }
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val picker =
        rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
            if (uris.isEmpty()) return@rememberLauncherForActivityResult
            scope.launch {
                val message =
                    when (viewModel.send(uris)) {
                        SendResult.STARTED -> R.string.share_sending
                        SendResult.NOTHING_TO_SEND -> R.string.share_nothing
                        SendResult.NEEDS_WIFI -> R.string.share_needs_wifi
                        SendResult.UNSUPPORTED -> ConnectionR.string.pc_update_needed
                        SendResult.NOT_CONNECTED -> ConnectionR.string.pc_not_connected
                    }
                Toast.makeText(context, message, Toast.LENGTH_LONG).show()
            }
        }
    TransferDetailScreen(
        state = state,
        onBack = onBack,
        actions =
            TransferDetailActions(
                open = {
                    state.record?.uri?.let { uri ->
                        val intent =
                            Intent(
                                Intent.ACTION_VIEW,
                                uri.toUri(),
                            ).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        runCatching { context.startActivity(intent) }
                    }
                },
                share = {
                    state.record?.uri?.let { uri ->
                        val send =
                            Intent(Intent.ACTION_SEND)
                                .setType(context.contentResolver.getType(uri.toUri()) ?: "*/*")
                                .putExtra(Intent.EXTRA_STREAM, uri.toUri())
                                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        runCatching { context.startActivity(Intent.createChooser(send, null)) }
                    }
                },
                sendAgain = { picker.launch(arrayOf("*/*")) },
                cancel = viewModel::cancel,
            ),
        modifier = modifier,
    )
}

/** What the buttons on the transfer screen do. */
class TransferDetailActions(
    val open: () -> Unit = {},
    val share: () -> Unit = {},
    val sendAgain: () -> Unit = {},
    val cancel: () -> Unit = {},
)

/**
 * One transfer (design: not drawn; the app's sub-screen bar, cards and buttons): status, direction,
 * size, time and where the file is. A received file that is still on the phone opens or is shared
 * from here; a send that did not finish is sent again by choosing the files again, because Fenpo
 * keeps no copy of what it sends. Every row on the transfer lists opens this screen.
 */
@Composable
fun TransferDetailScreen(
    state: TransferDetailUiState,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    actions: TransferDetailActions = TransferDetailActions(),
) {
    val active = state.active
    val record = state.record
    val fileName = active?.fileName ?: record?.fileName
    Column(modifier = modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        FenpoSubTopBar(title = stringResource(R.string.transfer_detail_title), onBack = onBack)
        if (fileName == null) {
            NoteBox(
                text = stringResource(R.string.transfer_detail_gone),
                icon = FenpoIcons.Info,
                emphasized = false,
                modifier = Modifier.padding(16.dp),
            )
            return@Column
        }
        val incoming = active?.incoming ?: record?.incoming ?: false
        FenpoCard(modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
            FenpoListRow(
                title = fileName,
                supporting =
                    stringResource(
                        if (incoming) R.string.transfer_detail_from_pc else R.string.transfer_detail_to_pc,
                    ),
                leading = { TransferBadge(fileName) },
            )
            if (active != null) {
                val fraction = if (active.totalBytes > 0) active.bytesDone.toFloat() / active.totalBytes else 0f
                LinearProgressIndicator(
                    progress = { fraction },
                    modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 24.dp, bottom = 16.dp),
                )
            }
        }
        FenpoCard(modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
            val context = LocalContext.current
            InfoRow(R.string.transfer_detail_status, status(active != null, record?.outcome))
            val count = active?.fileCount ?: record?.fileCount ?: 1
            if (count > 1) {
                RowDivider()
                InfoRow(R.string.transfer_detail_files, pluralStringResource(R.plurals.transfers_count, count, count))
            }
            val bytes = active?.totalBytes ?: record?.bytes ?: 0L
            if (bytes > 0) {
                RowDivider()
                InfoRow(R.string.transfer_detail_size, Formatter.formatShortFileSize(context, bytes))
            }
            if (record != null) {
                RowDivider()
                InfoRow(R.string.transfer_detail_time, dateTime(record.at))
            }
            RowDivider()
            InfoRow(
                R.string.transfer_detail_location,
                stringResource(if (incoming) R.string.transfer_detail_downloads else R.string.transfer_detail_pc),
            )
        }
        Column(
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            when {
                active != null -> {
                    FilledTonalButton(onClick = actions.cancel, modifier = Modifier.fillMaxWidth()) {
                        Text(stringResource(R.string.transfers_cancel))
                    }
                }

                record == null -> {
                    Unit
                }

                incoming && record.outcome == TransferOutcome.COMPLETED && state.fileExists -> {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Button(onClick = actions.open, modifier = Modifier.weight(1f)) {
                            Text(stringResource(R.string.transfer_detail_open))
                        }
                        FilledTonalButton(onClick = actions.share, modifier = Modifier.weight(1f)) {
                            Text(stringResource(R.string.transfer_detail_share))
                        }
                    }
                    if (record.fileCount > 1) {
                        Text(
                            stringResource(R.string.transfer_detail_others),
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }

                incoming && record.outcome == TransferOutcome.COMPLETED -> {
                    NoteBox(
                        text = stringResource(R.string.transfer_detail_missing),
                        icon = FenpoIcons.Info,
                        emphasized = false,
                    )
                }

                !incoming && record.outcome != TransferOutcome.COMPLETED -> {
                    Button(onClick = actions.sendAgain, modifier = Modifier.fillMaxWidth()) {
                        Text(stringResource(R.string.transfer_detail_send_again))
                    }
                    Text(
                        stringResource(R.string.transfer_detail_send_again_note),
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }

                else -> {
                    Unit
                }
            }
        }
    }
}

@Composable
private fun InfoRow(
    label: Int,
    value: String,
) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text(stringResource(label), style = MaterialTheme.typography.bodyLarge)
        Text(
            value,
            modifier = Modifier.weight(1f),
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.End,
        )
    }
}

@Composable
private fun status(
    moving: Boolean,
    outcome: TransferOutcome?,
): String =
    stringResource(
        when {
            moving -> R.string.transfer_detail_in_progress
            outcome == TransferOutcome.COMPLETED -> R.string.transfer_detail_completed
            outcome == TransferOutcome.DECLINED -> R.string.transfer_detail_declined
            outcome == TransferOutcome.CANCELLED -> R.string.transfer_detail_cancelled
            else -> R.string.transfer_detail_failed
        },
    )

@Composable
private fun dateTime(at: Instant): String {
    val locale = ConfigurationCompat.getLocales(LocalConfiguration.current)[0] ?: java.util.Locale.ROOT
    return DateTimeFormatter
        .ofLocalizedDateTime(FormatStyle.MEDIUM, FormatStyle.SHORT)
        .withLocale(locale)
        .withZone(ZoneId.systemDefault())
        .format(at)
}
