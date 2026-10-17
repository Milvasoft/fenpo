package com.fenpo.android.feature.remote

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureStoreScreen
import com.fenpo.android.core.testing.captureVideoScene
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

/** Google Play screenshots and video scene of the touchpad and the presentation remote (design/store/android/PLAN.md). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StoreScreenshotTest {
    @Test
    fun touchpad() =
        captureStoreScreen("04-touchpad") {
            TouchpadScreen(available = true, onBack = {}, onText = {}, onKey = {})
        }

    @Test
    fun presenter() =
        captureStoreScreen("05-presenter") {
            PresenterScreen(available = true, onBack = {}, onAction = {}, onLaser = { _, _, _ -> })
        }

    @Test
    fun touchpadScene() =
        captureVideoScene("04-touchpad") {
            TouchpadScreen(available = true, onBack = {}, onText = {}, onKey = {})
        }
}
