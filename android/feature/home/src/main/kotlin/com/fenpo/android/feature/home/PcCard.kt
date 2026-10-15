package com.fenpo.android.feature.home

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.fenpo.android.core.designsystem.component.FenpoCard
import com.fenpo.android.core.designsystem.component.LinkChip
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.designsystem.theme.FenpoTheme

/** The paired PC: connection state, name, operating system and active connection paths. */
@Composable
internal fun PcCard(
    pc: PcCardState,
    modifier: Modifier = Modifier,
) {
    val glow = MaterialTheme.colorScheme.primary.copy(alpha = 0.22f)
    FenpoCard(modifier = modifier.fillMaxWidth()) {
        Column(
            modifier =
                Modifier
                    .drawBehind {
                        val radius = 120.dp.toPx()
                        drawCircle(
                            brush =
                                Brush.radialGradient(
                                    listOf(glow, Color.Transparent),
                                    Offset(size.width - 60.dp.toPx(), 40.dp.toPx()),
                                    radius,
                                ),
                            radius = radius,
                            center = Offset(size.width - 60.dp.toPx(), 40.dp.toPx()),
                        )
                    }.padding(20.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                LaptopIllustration()
                Column {
                    ConnectionState(connected = pc.connected)
                    Text(text = pc.name, style = MaterialTheme.typography.titleLarge)
                    Text(
                        text =
                            when {
                                pc.osLabel != null -> stringResource(R.string.pc_os_at_desk, pc.osLabel)
                                else -> stringResource(R.string.pc_waiting_for_connection)
                            },
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
            if (pc.bluetooth || pc.wifi) {
                Row(modifier = Modifier.padding(top = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    LinkChip(
                        label = stringResource(R.string.link_bluetooth),
                        icon = FenpoIcons.Bluetooth,
                        active = pc.bluetooth,
                    )
                    LinkChip(label = stringResource(R.string.link_wifi), icon = FenpoIcons.Wifi, active = pc.wifi)
                }
            }
        }
    }
}

/** Shown instead of [PcCard] before the first pairing. */
@Composable
internal fun NotPairedCard(
    onPair: () -> Unit,
    modifier: Modifier = Modifier,
) {
    FenpoCard(modifier = modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(20.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                LaptopIllustration()
                Text(text = stringResource(R.string.not_paired_title), style = MaterialTheme.typography.titleMedium)
            }
            Text(
                text = stringResource(R.string.not_paired_body),
                modifier = Modifier.padding(top = 12.dp),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Button(onClick = onPair, modifier = Modifier.padding(top = 16.dp)) {
                Text(stringResource(R.string.not_paired_action))
            }
        }
    }
}

@Composable
private fun ConnectionState(connected: Boolean) {
    val color = if (connected) FenpoTheme.extendedColors.success else MaterialTheme.colorScheme.onSurfaceVariant
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Box(
            Modifier
                .size(8.dp)
                .clip(CircleShape)
                .background(color),
        )
        Text(
            text = stringResource(if (connected) R.string.pc_connected else R.string.pc_not_connected),
            style = MaterialTheme.typography.labelLarge,
            color = color,
        )
    }
}

/** Laptop drawing from the design: screen with the Cam-to-Çiçek gradient and a base. */
@Composable
private fun LaptopIllustration() {
    val frame = FenpoTheme.extendedColors.deviceFrame
    val screen =
        Brush.linearGradient(
            listOf(MaterialTheme.colorScheme.surfaceContainerHighest, MaterialTheme.colorScheme.primaryContainer),
        )
    Box(modifier = Modifier.padding(horizontal = 9.dp).padding(bottom = 6.dp)) {
        Box(
            Modifier
                .size(width = 64.dp, height = 44.dp)
                .clip(RoundedCornerShape(6.dp))
                .background(screen)
                .border(3.dp, frame, RoundedCornerShape(6.dp)),
        )
        Box(
            Modifier
                .align(Alignment.BottomCenter)
                .offset(y = 6.dp)
                .size(width = 82.dp, height = 5.dp)
                .clip(RoundedCornerShape(bottomStart = 5.dp, bottomEnd = 5.dp))
                .background(frame),
        )
    }
}
