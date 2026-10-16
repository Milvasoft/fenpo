package com.fenpo.android.feature.home

import android.widget.Toast
import androidx.annotation.DrawableRes
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.FilledTonalIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.connection.clipboard.ClipboardSendActivity
import com.fenpo.android.core.designsystem.component.FenpoCard
import com.fenpo.android.core.designsystem.component.FenpoListRow
import com.fenpo.android.core.designsystem.component.FenpoRootTopBar
import com.fenpo.android.core.designsystem.component.LeadingIcon
import com.fenpo.android.core.designsystem.component.RowDivider
import com.fenpo.android.core.designsystem.component.SectionLabel
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.protocol.v1.MediaAction
import com.fenpo.protocol.v1.MediaState
import kotlinx.coroutines.launch
import com.fenpo.android.core.connection.R as ConnectionR

/** PC tab with its view models. */
@Composable
fun PcRoute(
    onPair: () -> Unit,
    modifier: Modifier = Modifier,
    onOpenTouchpad: () -> Unit = {},
    onOpenPresenter: () -> Unit = {},
    viewModel: HomeViewModel = hiltViewModel(),
    pcViewModel: PcViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    val tab by pcViewModel.state.collectAsStateWithLifecycle()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()

    fun run(
        done: Int,
        action: suspend () -> PcActionResult,
    ) {
        scope.launch {
            val message =
                when (action()) {
                    PcActionResult.DONE -> done
                    PcActionResult.NOT_CONNECTED -> ConnectionR.string.pc_not_connected
                    PcActionResult.UNSUPPORTED -> ConnectionR.string.pc_update_needed
                }
            if (message != 0) Toast.makeText(context, message, Toast.LENGTH_SHORT).show()
        }
    }
    PcScreen(
        pc = state.pc,
        tab = tab,
        onPair = onPair,
        onRing = { run(R.string.pc_ringing) { pcViewModel.ring() } },
        onLock = { run(ConnectionR.string.lock_pc_sent) { pcViewModel.lock() } },
        onSendClipboard = { context.startActivity(ClipboardSendActivity.intent(context)) },
        onMedia = { action -> run(0) { pcViewModel.media(action) } },
        onVolume = { up -> run(0) { pcViewModel.volume(up) } },
        onOpenTouchpad = onOpenTouchpad,
        onOpenPresenter = onOpenPresenter,
        modifier = modifier,
    )
}

/**
 * PC tab. The design does not detail it; it uses the home screen's card and rows: the PC card,
 * what the PC plays when it says so, and what the phone can ask of the PC.
 */
@Composable
fun PcScreen(
    pc: PcCardState?,
    onPair: () -> Unit,
    modifier: Modifier = Modifier,
    tab: PcTabState = PcTabState(),
    onRing: () -> Unit = {},
    onLock: () -> Unit = {},
    onSendClipboard: () -> Unit = {},
    onMedia: (MediaAction) -> Unit = {},
    onVolume: (Boolean) -> Unit = {},
    onOpenTouchpad: () -> Unit = {},
    onOpenPresenter: () -> Unit = {},
) {
    Column(modifier = modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        FenpoRootTopBar(title = stringResource(R.string.pc_title))
        Column(modifier = Modifier.padding(horizontal = 16.dp)) {
            if (pc != null) PcCard(pc) else NotPairedCard(onPair = onPair)
        }
        if (pc == null) return@Column
        tab.media?.let { media ->
            SectionLabel(stringResource(R.string.pc_now_playing))
            NowPlaying(media, onMedia, onVolume, Modifier.padding(horizontal = 16.dp))
        }
        SectionLabel(stringResource(R.string.pc_actions))
        FenpoCard(modifier = Modifier.padding(horizontal = 16.dp)) {
            ActionRow(
                FenpoIcons.Bell,
                R.string.pc_ring,
                R.string.pc_ring_summary,
                enabled = tab.canRing,
                onClick = onRing,
            )
            RowDivider()
            ActionRow(
                FenpoIcons.Lock,
                R.string.pc_lock,
                R.string.pc_lock_summary,
                enabled = tab.canLock,
                onClick = onLock,
            )
            RowDivider()
            ActionRow(
                FenpoIcons.Clipboard,
                R.string.action_send_clipboard,
                R.string.pc_clipboard_summary,
                onClick = onSendClipboard,
            )
            RowDivider()
            ActionRow(FenpoIcons.Pc, R.string.pc_touchpad, R.string.pc_touchpad_summary, onClick = onOpenTouchpad)
            RowDivider()
            ActionRow(FenpoIcons.Next, R.string.pc_presenter, R.string.pc_presenter_summary, onClick = onOpenPresenter)
        }
        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun NowPlaying(
    media: MediaState,
    onMedia: (MediaAction) -> Unit,
    onVolume: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
) {
    FenpoCard(modifier = modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Column {
                Text(
                    text = media.title.ifEmpty { media.app_name },
                    style = MaterialTheme.typography.titleMedium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                val detail =
                    listOf(
                        media.artist,
                        media.app_name,
                    ).filter { it.isNotBlank() }.distinct().joinToString(" · ")
                if (detail.isNotEmpty()) {
                    Text(
                        text = detail,
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                MediaButton(FenpoIcons.VolumeDown, R.string.pc_volume_down) { onVolume(false) }
                MediaButton(FenpoIcons.Previous, R.string.pc_previous, enabled = media.can_previous) {
                    onMedia(MediaAction.MEDIA_ACTION_PREVIOUS)
                }
                MediaButton(
                    if (media.playing) FenpoIcons.Pause else FenpoIcons.Play,
                    if (media.playing) R.string.pc_pause else R.string.pc_play,
                ) { onMedia(MediaAction.MEDIA_ACTION_TOGGLE) }
                MediaButton(
                    FenpoIcons.Next,
                    R.string.pc_next,
                    enabled = media.can_next,
                ) { onMedia(MediaAction.MEDIA_ACTION_NEXT) }
                MediaButton(FenpoIcons.VolumeUp, R.string.pc_volume_up) { onVolume(true) }
            }
        }
    }
}

@Composable
private fun MediaButton(
    @DrawableRes icon: Int,
    label: Int,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    FilledTonalIconButton(onClick = onClick, enabled = enabled) {
        Icon(painterResource(icon), contentDescription = stringResource(label))
    }
}

@Composable
private fun ActionRow(
    @DrawableRes icon: Int,
    title: Int,
    summary: Int,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    FenpoListRow(
        title = stringResource(title),
        supporting = stringResource(summary),
        leading = { LeadingIcon(icon) },
        modifier = Modifier.clickable(enabled = enabled, onClick = onClick),
    )
}
