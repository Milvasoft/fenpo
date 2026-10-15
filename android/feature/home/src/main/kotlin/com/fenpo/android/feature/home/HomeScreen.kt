package com.fenpo.android.feature.home

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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.designsystem.component.FenpoCard
import com.fenpo.android.core.designsystem.component.FenpoListRow
import com.fenpo.android.core.designsystem.component.FenpoRootTopBar
import com.fenpo.android.core.designsystem.component.FileTypeBadge
import com.fenpo.android.core.designsystem.component.IconTile
import com.fenpo.android.core.designsystem.component.RowDivider
import com.fenpo.android.core.designsystem.component.SectionLabel
import com.fenpo.android.core.designsystem.format.relativeTime
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import java.time.Instant
import java.util.Locale

/** Home tab with its view model. */
@Composable
fun HomeRoute(
    onPair: () -> Unit,
    onOpenNotificationSettings: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: HomeViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    HomeScreen(
        state = state,
        onPair = onPair,
        onOpenNotificationSettings = onOpenNotificationSettings,
        modifier = modifier,
    )
}

/**
 * Home screen (design: "Ana ekran"): the PC card, four quick actions, the notification mirroring
 * row and recent transfers. Quick actions are wired to the connection in phase C.
 */
@Composable
fun HomeScreen(
    state: HomeUiState,
    onPair: () -> Unit,
    onOpenNotificationSettings: () -> Unit,
    modifier: Modifier = Modifier,
    onQuickAction: (QuickAction) -> Unit = {},
) {
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState()),
    ) {
        FenpoRootTopBar(title = stringResource(R.string.home_title)) {
            IconButton(onClick = onPair) {
                Icon(painterResource(FenpoIcons.Plus), contentDescription = stringResource(R.string.home_add_pc))
            }
        }
        Column(modifier = Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (state.pc != null) PcCard(state.pc) else NotPairedCard(onPair = onPair)
            QuickActions(state = state, onQuickAction = onQuickAction)
            if (state.mirroring != null) {
                MirroringRow(summary = state.mirroring, now = state.now, onClick = onOpenNotificationSettings)
            }
        }
        if (state.transfers.isNotEmpty()) {
            SectionLabel(stringResource(R.string.recent_transfers))
            FenpoCard(modifier = Modifier.padding(horizontal = 16.dp)) {
                state.transfers.forEachIndexed { index, item ->
                    if (index > 0) RowDivider()
                    TransferRow(item = item, state = state)
                }
            }
        }
        Spacer(Modifier.height(16.dp))
    }
}

/** The four quick actions on the home screen. */
enum class QuickAction { SEND_CLIPBOARD, PASTE_FROM_PC, SEND_PHOTOS, CAMERA }

@Composable
private fun QuickActions(
    state: HomeUiState,
    onQuickAction: (QuickAction) -> Unit,
) {
    val colors = MaterialTheme.colorScheme
    val extended = FenpoTheme.extendedColors
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            QuickActionTile(
                icon = FenpoIcons.Clipboard,
                iconColors = colors.primary to colors.onPrimary,
                title = stringResource(R.string.action_send_clipboard),
                summary =
                    state.phoneClipboardPreview?.let { "“$it…”" }
                        ?: stringResource(R.string.action_send_clipboard_empty),
                onClick = { onQuickAction(QuickAction.SEND_CLIPBOARD) },
                modifier = Modifier.weight(1f),
            )
            QuickActionTile(
                icon = FenpoIcons.Paste,
                iconColors = colors.tertiary to colors.onTertiary,
                title = stringResource(R.string.action_paste_from_pc),
                summary = state.pcClipboardPreview ?: stringResource(R.string.action_paste_from_pc_empty),
                onClick = { onQuickAction(QuickAction.PASTE_FROM_PC) },
                modifier = Modifier.weight(1f),
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            QuickActionTile(
                icon = FenpoIcons.Photo,
                iconColors = extended.success to extended.onSuccess,
                title = stringResource(R.string.action_send_photos),
                summary =
                    if (state.recentPhotoCount > 0) {
                        pluralStringResource(
                            R.plurals.action_send_photos_ready,
                            state.recentPhotoCount,
                            state.recentPhotoCount,
                        )
                    } else {
                        stringResource(R.string.action_send_photos_empty)
                    },
                onClick = { onQuickAction(QuickAction.SEND_PHOTOS) },
                modifier = Modifier.weight(1f),
            )
            QuickActionTile(
                icon = FenpoIcons.Camera,
                iconColors = colors.inverseSurface to colors.inverseOnSurface,
                title = stringResource(R.string.action_camera),
                summary = stringResource(R.string.action_camera_summary),
                onClick = { onQuickAction(QuickAction.CAMERA) },
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@Composable
private fun QuickActionTile(
    @DrawableRes icon: Int,
    iconColors: Pair<Color, Color>,
    title: String,
    summary: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Surface(
        modifier =
            modifier
                .height(104.dp)
                .clip(RoundedCornerShape(24.dp))
                .clickable(onClick = onClick),
        color = FenpoTheme.extendedColors.card,
    ) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.SpaceBetween) {
            IconTile(icon = icon, containerColor = iconColors.first, contentColor = iconColors.second)
            Column {
                // Tighter title line height so icon, title and summary fit the 104 dp tile of the design.
                Text(text = title, style = MaterialTheme.typography.titleMedium.copy(lineHeight = 20.sp))
                Text(
                    text = summary,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

@Composable
private fun MirroringRow(
    summary: MirroringSummary,
    now: Instant,
    onClick: () -> Unit,
) {
    val apps = pluralStringResource(R.plurals.mirroring_apps, summary.appCount, summary.appCount)
    val text =
        if (summary.lastSender != null && summary.lastAt != null) {
            stringResource(R.string.mirroring_apps_last, apps, summary.lastSender, relativeTime(summary.lastAt, now))
        } else {
            apps
        }
    FenpoCard(modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).clickable(onClick = onClick)) {
        Row(
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Surface(
                modifier = Modifier.size(40.dp),
                shape = CircleShape,
                color = MaterialTheme.colorScheme.secondaryContainer,
                contentColor = MaterialTheme.colorScheme.onSecondaryContainer,
            ) {
                Icon(painterResource(FenpoIcons.Bell), contentDescription = null, modifier = Modifier.padding(8.dp))
            }
            Column(modifier = Modifier.weight(1f)) {
                Text(text = stringResource(R.string.mirroring_title), style = MaterialTheme.typography.titleMedium)
                Text(
                    text = text,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Icon(
                painterResource(FenpoIcons.Chevron),
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun TransferRow(
    item: TransferItem,
    state: HomeUiState,
) {
    val extension =
        item.fileName
            .substringAfterLast('.', "")
            .uppercase(Locale.ROOT)
            .take(4)
    val isImage = extension in setOf("JPG", "JPEG", "PNG", "HEIC", "WEBP", "GIF")
    val colors = MaterialTheme.colorScheme
    val detail =
        if (item.incoming) {
            relativeTime(item.at, state.now)
        } else {
            item.folder
                ?: relativeTime(item.at, state.now)
        }
    FenpoListRow(
        title = item.fileName,
        supporting = stringResource(if (item.incoming) R.string.transfer_from_pc else R.string.transfer_to_pc, detail),
        leading = {
            FileTypeBadge(
                label = extension,
                containerColor = if (isImage) colors.tertiaryContainer else colors.primaryContainer,
                contentColor = if (isImage) colors.onTertiaryContainer else colors.onPrimaryContainer,
            )
        },
    )
}
