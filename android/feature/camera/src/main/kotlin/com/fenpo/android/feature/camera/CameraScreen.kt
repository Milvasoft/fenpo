package com.fenpo.android.feature.camera

import androidx.annotation.DrawableRes
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.protocol.v1.CameraFacing
import com.fenpo.protocol.v1.CameraSettings
import com.fenpo.protocol.v1.CameraState
import java.text.NumberFormat

/**
 * Webcam mode (design: "Kamera modu"), always dark: the preview fills the screen, the top shows
 * who uses the camera and the path, the bottom panel holds zoom and the four switches. While
 * dimmed the screen is black; a tap brings it back.
 *
 * @param preview The camera preview; a plain background in screenshot tests.
 */
@Composable
fun CameraScreen(
    state: CameraUiState,
    onSettingsChange: (CameraSettings) -> Unit,
    onDim: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
    preview: @Composable () -> Unit = {},
) {
    val colors = MaterialTheme.colorScheme
    Box(modifier = modifier.fillMaxSize().background(colors.surfaceContainerLowest)) {
        preview()
        if (state.dimmed) {
            Box(
                modifier =
                    Modifier
                        .fillMaxSize()
                        .background(colors.scrim)
                        .clickable { onDim(false) },
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    stringResource(R.string.camera_dimmed_hint),
                    color = colors.onSurfaceVariant,
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
            return@Box
        }
        Row(
            modifier = Modifier.fillMaxWidth().statusBarsPadding().padding(horizontal = 16.dp, vertical = 12.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Pill(text = statusText(state), live = state.state == CameraState.CAMERA_STATE_STREAMING)
            Pill(text = stringResource(R.string.camera_path_wifi), icon = FenpoIcons.Wifi)
        }
        Controls(
            state = state,
            onSettingsChange = onSettingsChange,
            onDim = { onDim(true) },
            modifier =
                Modifier
                    .align(
                        Alignment.BottomCenter,
                    ).navigationBarsPadding()
                    .padding(start = 12.dp, end = 12.dp, bottom = 32.dp),
        )
    }
}

@Composable
private fun statusText(state: CameraUiState): String =
    when (state.state) {
        CameraState.CAMERA_STATE_STREAMING -> {
            if (state.consumer.isEmpty()) {
                stringResource(R.string.camera_live)
            } else {
                stringResource(R.string.camera_live_app, state.consumer)
            }
        }

        CameraState.CAMERA_STATE_STARTING, CameraState.CAMERA_STATE_WAITING_FOR_TAP -> {
            stringResource(R.string.camera_starting)
        }

        CameraState.CAMERA_STATE_FAILED -> {
            stringResource(R.string.camera_failed)
        }

        else -> {
            stringResource(R.string.camera_idle)
        }
    }

@Composable
private fun Pill(
    text: String,
    live: Boolean = false,
    @DrawableRes icon: Int? = null,
) {
    val colors = MaterialTheme.colorScheme
    Row(
        modifier =
            Modifier
                .heightIn(min = 36.dp)
                .clip(RoundedCornerShape(18.dp))
                .background(colors.surface.copy(alpha = PILL_ALPHA))
                .padding(horizontal = 14.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        if (live) Box(Modifier.size(8.dp).clip(CircleShape).background(colors.primary))
        if (icon != null) {
            Icon(
                painterResource(icon),
                contentDescription = null,
                modifier = Modifier.size(18.dp),
                tint = colors.onSurface,
            )
        }
        Text(text, style = MaterialTheme.typography.labelLarge, color = colors.onSurface)
    }
}

@Composable
private fun Controls(
    state: CameraUiState,
    onSettingsChange: (CameraSettings) -> Unit,
    onDim: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = MaterialTheme.colorScheme
    val settings = state.settings
    Column(
        modifier =
            modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(28.dp))
                .background(colors.surface.copy(alpha = PANEL_ALPHA))
                .padding(16.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally),
        ) {
            state.zoomPresets.forEach { zoom ->
                val selected = kotlin.math.abs(settings.zoom - zoom) < ZOOM_MATCH
                val background =
                    if (selected) {
                        colors.onPrimaryContainer
                    } else {
                        colors.onSurface.copy(
                            alpha = BUTTON_ALPHA,
                        )
                    }
                val foreground = if (selected) colors.primaryContainer else colors.onSurface
                Box(
                    modifier =
                        Modifier
                            .size(44.dp)
                            .clip(CircleShape)
                            .background(background)
                            .clickable(role = Role.Button) { onSettingsChange(settings.copy(zoom = zoom)) },
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        zoomLabel(zoom),
                        style = MaterialTheme.typography.labelLarge,
                        color = foreground,
                    )
                }
            }
        }
        Spacer(Modifier.height(14.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            val front = settings.facing == CameraFacing.CAMERA_FACING_FRONT
            val flipped = if (front) CameraFacing.CAMERA_FACING_BACK else CameraFacing.CAMERA_FACING_FRONT
            Toggle(
                icon = FenpoIcons.Flip,
                label = R.string.camera_flip,
                on = false,
                onClick = { onSettingsChange(settings.copy(facing = flipped)) },
                modifier = Modifier.weight(1f),
                enabled = state.frontAvailable,
            )
            Toggle(
                icon = FenpoIcons.Dim,
                label = R.string.camera_dim,
                on = false,
                onClick = onDim,
                modifier = Modifier.weight(1f),
            )
        }
    }
}

@Composable
private fun Toggle(
    @DrawableRes icon: Int,
    label: Int,
    on: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val colors = MaterialTheme.colorScheme
    Column(
        modifier = modifier,
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(
            modifier =
                Modifier
                    .fillMaxWidth()
                    .height(56.dp)
                    .clip(RoundedCornerShape(18.dp))
                    .background(if (on) colors.primary else colors.onSurface.copy(alpha = BUTTON_ALPHA))
                    .clickable(enabled = enabled, role = Role.Switch, onClick = onClick),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painterResource(icon),
                contentDescription = null,
                tint = if (on) colors.onPrimary else colors.onSurface,
            )
        }
        Text(
            stringResource(label),
            style = MaterialTheme.typography.labelMedium,
            color = colors.onSurface,
            textAlign = TextAlign.Center,
            maxLines = 2,
        )
    }
}

/** "0,5", "1×", "2" as in the design, with the locale's decimal separator. */
@Composable
private fun zoomLabel(zoom: Float): String {
    val format =
        NumberFormat
            .getNumberInstance(
                LocalConfiguration.current.locales[0],
            ).apply { maximumFractionDigits = 1 }
    return if (zoom == 1f) "1×" else format.format(zoom)
}

private const val PILL_ALPHA = 0.6f
private const val PANEL_ALPHA = 0.82f
private const val BUTTON_ALPHA = 0.12f
private const val ZOOM_MATCH = 0.05f
