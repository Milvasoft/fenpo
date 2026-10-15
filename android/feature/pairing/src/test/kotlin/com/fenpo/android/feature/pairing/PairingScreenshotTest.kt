package com.fenpo.android.feature.pairing

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.data.permissions.AppPermission
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
            ScannerScreen(onBack = {}, onEnterCode = {})
        }

    @Test
    fun pairingSteps() =
        captureScreenVariants("pairing_steps") {
            PairingStepsScreen(
                state =
                    PairingStepsState(
                        pcName = "BUGRA-PC",
                        wifi = StepStatus.DONE,
                        device = StepStatus.ACTIVE,
                        bluetooth = StepStatus.QUEUED,
                        permissions = StepStatus.QUEUED,
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
