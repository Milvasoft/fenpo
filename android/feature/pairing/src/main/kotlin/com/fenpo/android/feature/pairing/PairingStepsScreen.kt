package com.fenpo.android.feature.pairing

import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.data.permissions.AppPermission
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.fenpo.android.core.pairing.PairingError

/** Progress of one pairing step. */
enum class StepStatus { DONE, ACTIVE, QUEUED, FAILED }

/**
 * Where pairing stands after the QR code was read.
 *
 * @property pcName PC name from the code, shown in the Wi‑Fi step.
 * @property error Why the Wi‑Fi step failed, if it did.
 * @property paired The PC is stored; the flow continues with permissions.
 */
data class PairingStepsState(
    val pcName: String,
    val wifi: StepStatus,
    val device: StepStatus,
    val bluetooth: StepStatus,
    val permissions: StepStatus,
    val error: PairingError? = null,
    val paired: Boolean = false,
)

/**
 * Steps screen with its view model. On Android 17 the local network permission is asked first
 * (it shares the "Nearby devices" dialog, docs/ANDROID.md §3); pairing starts once it is answered.
 */
@Composable
fun PairingStepsRoute(
    code: String,
    onBack: () -> Unit,
    onRescan: () -> Unit,
    onOpenPermissions: () -> Unit,
    onDone: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val viewModel =
        hiltViewModel<PairingViewModel, PairingViewModel.Factory>(key = code) { factory -> factory.create(code) }
    val state by viewModel.state.collectAsStateWithLifecycle()
    val context = LocalContext.current
    val nearby = AppPermission.NEARBY_DEVICES.runtimePermissions
    val launcher =
        rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { viewModel.start() }
    LaunchedEffect(Unit) {
        val missing =
            nearby.filter {
                ContextCompat.checkSelfPermission(context, it) !=
                    PackageManager.PERMISSION_GRANTED
            }
        if (Build.VERSION.SDK_INT >= ANDROID_17 &&
            missing.isNotEmpty()
        ) {
            launcher.launch(missing.toTypedArray())
        } else {
            viewModel.start()
        }
    }
    PairingStepsScreen(
        state = state,
        onBack = onBack,
        onRescan = onRescan,
        onOpenPermissions = onOpenPermissions,
        onDone = onDone,
        modifier = modifier,
    )
}

private const val ANDROID_17 = 37

/**
 * "Almost done" (design: "Bilgisayarı tanıtma"). The system's companion device and Bluetooth
 * pairing dialogs will appear on top of this screen once the BLE steps exist.
 */
@Composable
fun PairingStepsScreen(
    state: PairingStepsState,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    onRescan: () -> Unit = {},
    onOpenPermissions: () -> Unit = {},
    onDone: () -> Unit = {},
) {
    Column(modifier = modifier.fillMaxSize()) {
        FenpoSubTopBar(title = stringResource(R.string.steps_title), onBack = onBack)
        Column(modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) {
            Step(
                icon = FenpoIcons.Wifi,
                title = stringResource(R.string.step_wifi),
                status = state.wifi,
                doneText = stringResource(R.string.step_wifi_done, state.pcName),
                activeText = stringResource(R.string.step_connecting),
                failedText = state.error?.let { stringResource(it.title()) },
                busy = state.wifi == StepStatus.ACTIVE,
            )
            Step(icon = FenpoIcons.Pc, title = stringResource(R.string.step_device), status = state.device)
            Step(icon = FenpoIcons.Bluetooth, title = stringResource(R.string.step_bluetooth), status = state.bluetooth)
            Step(
                icon = FenpoIcons.Bell,
                title = stringResource(R.string.step_permissions),
                status = state.permissions,
                activeText = stringResource(R.string.step_permissions_next),
            )
        }
        Spacer(Modifier.weight(1f))
        Column(modifier = Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            when {
                state.error != null -> {
                    Text(
                        text = stringResource(state.error.fix()),
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(bottom = 8.dp),
                    )
                    Button(onClick = onRescan, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                        Text(stringResource(R.string.pairing_rescan), style = MaterialTheme.typography.titleMedium)
                    }
                }

                state.paired -> {
                    Button(onClick = onOpenPermissions, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                        Text(
                            stringResource(R.string.pairing_open_permissions),
                            style = MaterialTheme.typography.titleMedium,
                        )
                    }
                    TextButton(
                        onClick = onDone,
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text(stringResource(R.string.pairing_later)) }
                }
            }
        }
    }
}

@Composable
private fun Step(
    @DrawableRes icon: Int,
    title: String,
    status: StepStatus,
    doneText: String = stringResource(R.string.step_done),
    activeText: String = stringResource(R.string.step_waiting),
    failedText: String? = null,
    busy: Boolean = false,
) {
    val colors = MaterialTheme.colorScheme
    val extended = FenpoTheme.extendedColors
    val (container, content) =
        when (status) {
            StepStatus.DONE -> extended.successContainer to extended.onSuccessContainer
            StepStatus.ACTIVE -> colors.primaryContainer to colors.onPrimaryContainer
            StepStatus.QUEUED -> colors.surfaceContainerHighest to colors.onSurfaceVariant
            StepStatus.FAILED -> colors.errorContainer to colors.onErrorContainer
        }
    Row(
        modifier = Modifier.padding(vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Surface(modifier = Modifier.size(40.dp), shape = CircleShape, color = container, contentColor = content) {
            Icon(
                painter = painterResource(if (status == StepStatus.DONE) FenpoIcons.Check else icon),
                contentDescription = null,
                modifier = Modifier.padding(8.dp),
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(text = title, style = MaterialTheme.typography.bodyLarge)
            Text(
                text =
                    when (status) {
                        StepStatus.DONE -> doneText
                        StepStatus.ACTIVE -> activeText
                        StepStatus.QUEUED -> stringResource(R.string.step_queued)
                        StepStatus.FAILED -> failedText ?: stringResource(R.string.step_failed)
                    },
                style = MaterialTheme.typography.bodyMedium,
                color = if (status == StepStatus.FAILED) colors.error else colors.onSurfaceVariant,
            )
        }
        if (busy) {
            CircularProgressIndicator(modifier = Modifier.size(20.dp), strokeWidth = 2.dp)
        }
    }
}

@StringRes
private fun PairingError.title(): Int =
    when (this) {
        PairingError.UNREACHABLE -> R.string.pairing_error_unreachable
        PairingError.CERTIFICATE_MISMATCH -> R.string.pairing_error_certificate
        PairingError.HANDSHAKE_FAILED -> R.string.pairing_error_handshake
        PairingError.TOKEN_EXPIRED -> R.string.pairing_error_token_expired
        PairingError.TOKEN_INVALID -> R.string.pairing_error_token_invalid
        PairingError.REJECTED_BY_USER -> R.string.pairing_error_rejected
        PairingError.PROTOCOL_MISMATCH -> R.string.pairing_error_protocol
        PairingError.TIMED_OUT -> R.string.pairing_error_timeout
        PairingError.UNEXPECTED_RESPONSE -> R.string.pairing_error_unexpected
    }

@StringRes
private fun PairingError.fix(): Int =
    when (this) {
        PairingError.UNREACHABLE -> R.string.pairing_fix_unreachable
        PairingError.CERTIFICATE_MISMATCH -> R.string.pairing_fix_certificate
        PairingError.HANDSHAKE_FAILED -> R.string.pairing_fix_handshake
        PairingError.TOKEN_EXPIRED, PairingError.TOKEN_INVALID -> R.string.pairing_fix_new_code
        PairingError.REJECTED_BY_USER -> R.string.pairing_fix_rejected
        PairingError.PROTOCOL_MISMATCH -> R.string.pairing_fix_update
        PairingError.TIMED_OUT -> R.string.pairing_fix_timeout
        PairingError.UNEXPECTED_RESPONSE -> R.string.pairing_fix_unexpected
    }
