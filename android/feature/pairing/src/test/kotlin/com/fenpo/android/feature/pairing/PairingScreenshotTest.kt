package com.fenpo.android.feature.pairing

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.data.permissions.AppPermission
import com.fenpo.android.core.pairing.PairingError
import com.fenpo.android.core.testing.captureScreenVariants
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class PairingScreenshotTest {
    @Test
    fun scanner() =
        captureScreenVariants("scanner", fixedDark = true) {
            ScannerScreen(onBack = {})
        }

    @Test
    fun scannerWithoutCamera() =
        captureScreenVariants("scanner_camera_needed", fixedDark = true) {
            ScannerScreen(onBack = {}, hint = ScannerHint.CAMERA_NEEDED)
        }

    private val steps =
        PairingStepsState(
            pcName = "BUGRA-PC",
            wifi = StepStatus.DONE,
            device = StepStatus.ACTIVE,
            bluetooth = StepStatus.QUEUED,
            permissions = StepStatus.QUEUED,
        )

    /** The state drawn in the design. */
    @Test
    fun pairingSteps() = captureScreenVariants("pairing_steps") { PairingStepsScreen(state = steps, onBack = {}) }

    /** What phase C shows today after a successful Wi‑Fi pairing. */
    @Test
    fun pairingStepsPaired() =
        captureScreenVariants("pairing_steps_paired") {
            PairingStepsScreen(
                state = steps.copy(device = StepStatus.QUEUED, permissions = StepStatus.ACTIVE, paired = true),
                onBack = {},
            )
        }

    @Test
    fun pairingStepsFailed() =
        captureScreenVariants("pairing_steps_failed") {
            PairingStepsScreen(
                state =
                    steps.copy(
                        wifi = StepStatus.FAILED,
                        device = StepStatus.QUEUED,
                        error = PairingError.UNREACHABLE,
                    ),
                onBack = {},
            )
        }

    @Test
    fun pairingStepsBluetoothFailed() =
        captureScreenVariants("pairing_steps_bluetooth_failed") {
            PairingStepsScreen(
                state =
                    steps.copy(
                        device = StepStatus.DONE,
                        bluetooth = StepStatus.FAILED,
                        linkError = LinkError.BLUETOOTH_FAILED,
                    ),
                onBack = {},
            )
        }

    /** The state drawn in the design: notification access and battery missing. */
    @Test
    fun permissions() =
        captureScreenVariants("permissions") {
            PermissionsScreen(
                items =
                    AppPermission.entries.map {
                        PermissionItem(
                            it,
                            granted =
                                it != AppPermission.NOTIFICATION_ACCESS && it != AppPermission.BATTERY,
                        )
                    },
                onBack = {},
                onFix = {},
            )
        }
}
