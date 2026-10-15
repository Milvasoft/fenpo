package com.fenpo.android.feature.pairing

import androidx.annotation.DrawableRes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.designsystem.theme.FenpoTheme

/** Progress of one pairing step. */
enum class StepStatus { DONE, ACTIVE, QUEUED }

/**
 * Where pairing stands after the QR code was read.
 *
 * @property pcName PC name from the code, shown in the Wi‑Fi step.
 */
data class PairingStepsState(
    val pcName: String,
    val wifi: StepStatus,
    val device: StepStatus,
    val bluetooth: StepStatus,
    val permissions: StepStatus,
)

/**
 * "Almost done" (design: "Bilgisayarı tanıtma"). The system's companion device and Bluetooth
 * pairing dialogs appear on top of this screen; phase C drives the steps.
 */
@Composable
fun PairingStepsScreen(
    state: PairingStepsState,
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier.fillMaxSize()) {
        FenpoSubTopBar(title = stringResource(R.string.steps_title), onBack = onBack)
        Column(modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp)) {
            Step(
                icon = FenpoIcons.Wifi,
                title = stringResource(R.string.step_wifi),
                status = state.wifi,
                doneText = stringResource(R.string.step_wifi_done, state.pcName),
            )
            Step(icon = FenpoIcons.Pc, title = stringResource(R.string.step_device), status = state.device)
            Step(icon = FenpoIcons.Bluetooth, title = stringResource(R.string.step_bluetooth), status = state.bluetooth)
            Step(icon = FenpoIcons.Bell, title = stringResource(R.string.step_permissions), status = state.permissions)
        }
    }
}

@Composable
private fun Step(
    @DrawableRes icon: Int,
    title: String,
    status: StepStatus,
    doneText: String = stringResource(R.string.step_done),
) {
    val colors = MaterialTheme.colorScheme
    val extended = FenpoTheme.extendedColors
    val (container, content) =
        when (status) {
            StepStatus.DONE -> extended.successContainer to extended.onSuccessContainer
            StepStatus.ACTIVE -> colors.primaryContainer to colors.onPrimaryContainer
            StepStatus.QUEUED -> colors.surfaceContainerHighest to colors.onSurfaceVariant
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
        Column {
            Text(text = title, style = MaterialTheme.typography.bodyLarge)
            Text(
                text =
                    when (status) {
                        StepStatus.DONE -> doneText
                        StepStatus.ACTIVE -> stringResource(R.string.step_waiting)
                        StepStatus.QUEUED -> stringResource(R.string.step_queued)
                    },
                style = MaterialTheme.typography.bodyMedium,
                color = colors.onSurfaceVariant,
            )
        }
    }
}
