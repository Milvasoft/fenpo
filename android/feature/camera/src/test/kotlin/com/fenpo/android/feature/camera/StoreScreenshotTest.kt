package com.fenpo.android.feature.camera

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureStoreScreenshot
import com.fenpo.protocol.v1.CameraFacing
import com.fenpo.protocol.v1.CameraSettings
import com.fenpo.protocol.v1.CameraState
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

/**
 * Google Play screenshot of webcam mode (docs/android/PLAY-LISTING.md §2). The preview is a soft
 * gradient, not a photo of anyone.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StoreScreenshotTest {
    @Test
    fun camera() =
        captureStoreScreenshot("06-camera", dark = true) {
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
        }
}
