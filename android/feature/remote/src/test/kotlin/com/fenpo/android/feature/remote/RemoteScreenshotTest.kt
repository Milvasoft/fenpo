package com.fenpo.android.feature.remote

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureScreenVariants
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class RemoteScreenshotTest {
    @Test
    fun touchpad() =
        captureScreenVariants("touchpad") {
            TouchpadScreen(available = true, onBack = {}, onText = {}, onKey = {})
        }

    @Test
    fun presenter() =
        captureScreenVariants("presenter") {
            PresenterScreen(available = true, onBack = {}, onAction = {}, onLaser = { _, _, _ -> })
        }

    @Test
    fun presenterWithoutPcSupport() =
        captureScreenVariants("presenter_unavailable") {
            PresenterScreen(available = false, onBack = {}, onAction = {}, onLaser = { _, _, _ -> })
        }
}
