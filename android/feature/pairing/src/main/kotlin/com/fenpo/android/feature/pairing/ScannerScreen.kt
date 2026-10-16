package com.fenpo.android.feature.pairing

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import android.util.Log
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.StringRes
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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LifecycleResumeEffect
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.fenpo.android.core.pairing.PairingUri
import com.fenpo.android.core.pairing.PairingUriError
import com.fenpo.android.core.pairing.PairingUriResult

/** What the scanner says under its instructions. */
enum class ScannerHint {
    /** The camera permission is missing; the button asks for it. */
    CAMERA_NEEDED,

    /** The camera permission was denied for good; the button opens the app's settings. */
    CAMERA_BLOCKED,

    /** A QR code was read but it is not a Fenpo pairing code. */
    NOT_FENPO,

    /** The code comes from a newer Fenpo on the PC. */
    UPDATE_NEEDED,

    /** A Fenpo pairing code, but a field is missing or broken (bad print, partial read). */
    UNREADABLE,
}

private const val TAG = "FenpoScanner"

private fun PairingUriError.hint(): ScannerHint =
    when (this) {
        PairingUriError.NOT_FENPO -> ScannerHint.NOT_FENPO
        PairingUriError.UNSUPPORTED_VERSION -> ScannerHint.UPDATE_NEEDED
        else -> ScannerHint.UNREADABLE
    }

/** The code with every parameter value except `v` and `a` masked, for logs. */
private fun redact(text: String): String =
    text.replace(Regex("([?&])(id|n|fp|t)=[^&]*")) { "${it.groupValues[1]}${it.groupValues[2]}=…(${it.value.length})" }

/**
 * Scanner with camera permission handling. A valid code goes to [onCode]; anything else becomes
 * a hint. There is no manual code entry: a 6 digit code without the certificate fingerprint
 * cannot stop a man in the middle (S5; Buğra's decision, 16 October).
 */
@Composable
fun ScannerRoute(
    onBack: () -> Unit,
    onCode: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    var granted by rememberSaveable { mutableStateOf(false) }
    var asked by rememberSaveable { mutableStateOf(false) }
    var blocked by rememberSaveable { mutableStateOf(false) }
    var codeHint by rememberSaveable { mutableStateOf<ScannerHint?>(null) }
    var handled by rememberSaveable { mutableStateOf(false) }

    LifecycleResumeEffect(Unit) {
        granted =
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
        handled = false
        onPauseOrDispose {}
    }
    val launcher =
        rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { result ->
            granted = result
            // Android shows its dialog at most twice; a second refusal means only Settings can help.
            blocked = !result && asked
            asked = true
        }

    val hint =
        when {
            !granted && blocked -> ScannerHint.CAMERA_BLOCKED
            !granted -> ScannerHint.CAMERA_NEEDED
            else -> codeHint
        }
    ScannerScreen(
        onBack = onBack,
        hint = hint,
        onAllowCamera = {
            if (blocked) {
                val settings =
                    Intent(
                        Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                        Uri.fromParts("package", context.packageName, null),
                    )
                context.startActivity(settings.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            } else {
                launcher.launch(Manifest.permission.CAMERA)
            }
        },
        modifier = modifier,
        cameraPreview = {
            if (granted) {
                QrCameraPreview(onCode = { text ->
                    if (!handled) {
                        when (val result = PairingUri.parse(text)) {
                            is PairingUriResult.Valid -> {
                                handled = true
                                onCode(text)
                            }

                            is PairingUriResult.Invalid -> {
                                // Field names only; the token and fingerprint never reach the log.
                                Log.i(TAG, "Rejected code: ${result.error}, ${redact(text)}")
                                codeHint = result.error.hint()
                            }
                        }
                    }
                })
            } else {
                CameraBackdrop()
            }
        },
    )
}

/**
 * QR scanner (design: "Eşleştirme"). Always dark, like a camera.
 *
 * @param hint Extra line under the instructions; camera hints also show a button.
 * @param cameraPreview The live camera, or the dark backdrop of the design when there is none.
 */
@Composable
fun ScannerScreen(
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    hint: ScannerHint? = null,
    onAllowCamera: () -> Unit = {},
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
                        modifier = Modifier.padding(top = 8.dp, bottom = if (hint == null) 4.dp else 12.dp),
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    if (hint != null) {
                        Text(
                            text = stringResource(hint.text()),
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.primary,
                        )
                    }
                    if (hint == ScannerHint.CAMERA_NEEDED || hint == ScannerHint.CAMERA_BLOCKED) {
                        FilledTonalButton(
                            onClick = onAllowCamera,
                            modifier = Modifier.padding(top = 20.dp).fillMaxWidth().height(56.dp),
                        ) {
                            val label =
                                if (hint ==
                                    ScannerHint.CAMERA_BLOCKED
                                ) {
                                    R.string.scanner_open_settings
                                } else {
                                    R.string.scanner_allow_camera
                                }
                            Text(text = stringResource(label), style = MaterialTheme.typography.titleMedium)
                        }
                    }
                }
            }
        }
    }
}

@StringRes
private fun ScannerHint.text(): Int =
    when (this) {
        ScannerHint.CAMERA_NEEDED -> R.string.scanner_camera_needed
        ScannerHint.CAMERA_BLOCKED -> R.string.scanner_camera_blocked
        ScannerHint.NOT_FENPO -> R.string.scanner_not_fenpo
        ScannerHint.UPDATE_NEEDED -> R.string.scanner_update_needed
        ScannerHint.UNREADABLE -> R.string.scanner_unreadable
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
