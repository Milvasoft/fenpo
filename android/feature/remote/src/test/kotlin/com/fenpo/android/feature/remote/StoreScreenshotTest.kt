package com.fenpo.android.feature.remote

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureStoreScreenshot
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

/** Google Play screenshots of the touchpad and the presentation remote (docs/android/PLAY-LISTING.md §2). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StoreScreenshotTest {
    @Test
    fun touchpad() =
        captureStoreScreenshot("04-touchpad") {
            TouchpadScreen(available = true, onBack = {}, onText = {}, onKey = {})
        }

    @Test
    fun presenter() =
        captureStoreScreenshot("05-presenter") {
            PresenterScreen(available = true, onBack = {}, onAction = {}, onLaser = { _, _, _ -> })
        }
}
