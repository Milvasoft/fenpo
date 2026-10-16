package com.fenpo.android.feature.remote

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.component.NoteBox
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.protocol.v1.PcPresenterAction
import com.fenpo.android.core.connection.R as ConnectionR

/** Presentation remote with its view model; keeps both screens on while open. */
@Composable
fun PresenterRoute(
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: RemoteViewModel = hiltViewModel(),
) {
    val available by viewModel.available.collectAsStateWithLifecycle()
    val view = LocalView.current
    DisposableEffect(available) {
        view.keepScreenOn = true
        if (available) viewModel.keepAwake(true)
        onDispose {
            view.keepScreenOn = false
            if (available) viewModel.keepAwake(false)
        }
    }
    PresenterScreen(
        available = available,
        onBack = onBack,
        onAction = viewModel::presenter,
        onLaser = viewModel::laser,
        modifier = modifier,
    )
}

/**
 * Presentation remote (pc_input.proto). The design does not draw it; it uses the app's buttons and
 * card shapes: a large Next, Previous, start, end and black screen, and a pad that shows a laser
 * dot on the PC while held.
 */
@Composable
fun PresenterScreen(
    available: Boolean,
    onBack: () -> Unit,
    onAction: (PcPresenterAction) -> Unit,
    onLaser: (Boolean, Float, Float) -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier.fillMaxSize()) {
        FenpoSubTopBar(title = stringResource(R.string.presenter_title), onBack = onBack)
        if (!available) {
            NoteBox(
                text = stringResource(ConnectionR.string.pc_update_needed),
                icon = FenpoIcons.Info,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
            )
        }
        Column(
            modifier = Modifier.weight(1f).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Button(
                onClick = { onAction(PcPresenterAction.PC_PRESENTER_ACTION_NEXT) },
                modifier = Modifier.fillMaxWidth().weight(1f),
                shape = RoundedCornerShape(28.dp),
            ) {
                Icon(painterResource(FenpoIcons.Next), contentDescription = null)
                Text(
                    stringResource(R.string.presenter_next),
                    modifier = Modifier.padding(start = 12.dp),
                    style = MaterialTheme.typography.titleLarge,
                )
            }
            FilledTonalButton(
                onClick = { onAction(PcPresenterAction.PC_PRESENTER_ACTION_PREVIOUS) },
                modifier = Modifier.fillMaxWidth().height(72.dp),
                shape = RoundedCornerShape(24.dp),
            ) {
                Icon(painterResource(FenpoIcons.Previous), contentDescription = null)
                Text(stringResource(R.string.presenter_previous), modifier = Modifier.padding(start = 12.dp))
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilledTonalButton(
                    onClick = { onAction(PcPresenterAction.PC_PRESENTER_ACTION_START) },
                    modifier = Modifier.weight(1f),
                ) {
                    Text(stringResource(R.string.presenter_start))
                }
                FilledTonalButton(
                    onClick = { onAction(PcPresenterAction.PC_PRESENTER_ACTION_END) },
                    modifier = Modifier.weight(1f),
                ) {
                    Text(stringResource(R.string.presenter_end))
                }
                FilledTonalButton(
                    onClick = { onAction(PcPresenterAction.PC_PRESENTER_ACTION_BLACK) },
                    modifier = Modifier.weight(1f),
                ) {
                    Text(stringResource(R.string.presenter_black))
                }
            }
            Box(
                modifier =
                    Modifier
                        .fillMaxWidth()
                        .height(LASER_PAD_HEIGHT.dp)
                        .clip(RoundedCornerShape(24.dp))
                        .background(MaterialTheme.colorScheme.surfaceContainerHigh)
                        .pointerInput(Unit) {
                            awaitEachGesture {
                                val down = awaitFirstDown()
                                onLaser(true, down.position.x / size.width, down.position.y / size.height)
                                while (true) {
                                    val change = awaitPointerEvent().changes.firstOrNull() ?: break
                                    if (!change.pressed) break
                                    onLaser(true, change.position.x / size.width, change.position.y / size.height)
                                    change.consume()
                                }
                                onLaser(false, 0f, 0f)
                            }
                        },
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    stringResource(R.string.presenter_laser_hint),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.padding(16.dp),
                )
            }
        }
    }
}

private const val LASER_PAD_HEIGHT = 160
