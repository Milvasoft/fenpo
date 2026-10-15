package com.fenpo.android.feature.pairing

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.theme.FenpoTheme

/**
 * QR scanner (design: "Eşleştirme"). Always dark, like a camera. The camera preview and QR decoding
 * come in phase C; until then [cameraPreview] shows the dark backdrop of the design.
 */
@Composable
fun ScannerScreen(
    onBack: () -> Unit,
    onEnterCode: () -> Unit,
    modifier: Modifier = Modifier,
    cameraPreview: @Composable () -> Unit = { CameraBackdrop() },
) {
    FenpoTheme(darkTheme = true) {
        Box(modifier = modifier.fillMaxSize()) {
            cameraPreview()
            FinderFrame(modifier = Modifier.align(Alignment.Center).offset(y = (-90).dp))
            Surface(color = Color.Transparent, contentColor = MaterialTheme.colorScheme.onSurface) {
                FenpoSubTopBar(title = null, onBack = onBack, modifier = Modifier.statusBarsPadding())
            }
            Surface(
                modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth(),
                shape = RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp),
                color = MaterialTheme.colorScheme.surfaceContainerLow,
            ) {
                Column(
                    modifier = Modifier.navigationBarsPadding().padding(start = 24.dp, end = 24.dp, bottom = 20.dp),
                ) {
                    Box(
                        Modifier
                            .padding(top = 14.dp, bottom = 22.dp)
                            .align(Alignment.CenterHorizontally)
                            .size(width = 32.dp, height = 4.dp)
                            .clip(RoundedCornerShape(2.dp))
                            .background(MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.4f)),
                    )
                    Text(text = stringResource(R.string.scanner_title), style = MaterialTheme.typography.headlineSmall)
                    Text(
                        text = stringResource(R.string.scanner_body),
                        modifier = Modifier.padding(top = 8.dp, bottom = 24.dp),
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    FilledTonalButton(onClick = onEnterCode, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                        Text(
                            text = stringResource(R.string.scanner_enter_code),
                            style = MaterialTheme.typography.titleMedium,
                        )
                    }
                }
            }
        }
    }
}

/** Dark gradient behind the finder while the camera is not running. */
@Composable
private fun CameraBackdrop() {
    val colors = MaterialTheme.colorScheme
    Box(
        Modifier
            .fillMaxSize()
            .background(
                Brush.radialGradient(
                    0f to colors.surfaceBright,
                    0.6f to colors.surfaceContainerLow,
                    1f to colors.surfaceContainerLowest,
                ),
            ),
    )
}

/** The four white corners of the scanner, 236 dp square. */
@Composable
private fun FinderFrame(modifier: Modifier = Modifier) {
    val color = MaterialTheme.colorScheme.onSurface
    Canvas(modifier = modifier.size(236.dp)) {
        val stroke = 4.dp.toPx()
        val arm = 44.dp.toPx()
        val radius = 24.dp.toPx()
        val inset = stroke / 2
        val corners =
            listOf(
                Offset(inset, inset),
                Offset(size.width - inset - arm, inset),
                Offset(inset, size.height - inset - arm),
                Offset(size.width - inset - arm, size.height - inset - arm),
            )
        corners.forEachIndexed { index, origin ->
            // Draw a rounded square per corner and clip it to the corner's quarter.
            val clipLeft = index % 2 == 0
            val clipTop = index < 2
            val square = Size(arm * 2, arm * 2)
            val topLeft = Offset(if (clipLeft) origin.x else origin.x - arm, if (clipTop) origin.y else origin.y - arm)
            clipRect(
                left = origin.x - inset,
                top = origin.y - inset,
                right = origin.x + arm + inset,
                bottom = origin.y + arm + inset,
            ) {
                drawRoundRect(
                    color = color,
                    topLeft = topLeft,
                    size = square,
                    cornerRadius = CornerRadius(radius),
                    style = Stroke(stroke),
                )
            }
        }
    }
}
