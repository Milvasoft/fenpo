package com.fenpo.android.feature.camera

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.fenpo.android.core.testing.captureScreenVariants
import com.fenpo.protocol.v1.CameraFacing
import com.fenpo.protocol.v1.CameraSettings
import com.fenpo.protocol.v1.CameraState
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class CameraScreenshotTest {
    /** The state drawn in the design: live in Teams, 1×, center on. */
    private val live =
        CameraUiState(
            state = CameraState.CAMERA_STATE_STREAMING,
            consumer = "Teams",
            settings = CameraSettings(facing = CameraFacing.CAMERA_FACING_FRONT, zoom = 1f, center_stage = true),
            zoomPresets = listOf(0.5f, 1f, 2f),
        )

    @Test
    fun cameraLive() =
        captureScreenVariants("camera_live") {
            FenpoTheme(darkTheme = true) { CameraScreen(state = live, onSettingsChange = {}, onDim = {}) }
        }

    @Test
    fun cameraDimmed() =
        captureScreenVariants("camera_dimmed") {
            FenpoTheme(
                darkTheme = true,
            ) { CameraScreen(state = live.copy(dimmed = true), onSettingsChange = {}, onDim = {}) }
        }
}
