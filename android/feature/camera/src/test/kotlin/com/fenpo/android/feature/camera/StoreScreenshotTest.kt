package com.fenpo.android.feature.camera

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureStoreScreen
import com.fenpo.android.core.testing.captureVideoScene
import com.fenpo.protocol.v1.CameraFacing
import com.fenpo.protocol.v1.CameraSettings
import com.fenpo.protocol.v1.CameraState
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

/**
 * Google Play screenshot and video scene of webcam mode (design/store/android/PLAN.md). The
 * preview is a soft gradient, not a photo of anyone; the screen is always dark.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StoreScreenshotTest {
    @Composable
    private fun Camera() =
        CameraScreen(
            state =
                CameraUiState(
                    state = CameraState.CAMERA_STATE_STREAMING,
                    consumer = "",
                    settings =
                        CameraSettings(
                            facing = CameraFacing.CAMERA_FACING_FRONT,
                            zoom = 1f,
                            center_stage = true,
                        ),
                    zoomPresets = listOf(0.5f, 1f, 2f),
                ),
            onSettingsChange = {},
            onDim = {},
            preview = {
                // Marketing art only: Alacakaranlık into Çiçek, standing in for the camera picture.
                Box(
                    Modifier.fillMaxSize().background(
                        Brush.verticalGradient(listOf(Color(0xFF4E54C8), Color(0xFF8A5AA8), Color(0xFFE8577E))),
                    ),
                )
            },
        )

    @Test
    fun camera() = captureStoreScreen("06-camera", alwaysDark = true) { Camera() }

    @Test
    fun cameraScene() = captureVideoScene("06-camera", alwaysDark = true) { Camera() }
}
