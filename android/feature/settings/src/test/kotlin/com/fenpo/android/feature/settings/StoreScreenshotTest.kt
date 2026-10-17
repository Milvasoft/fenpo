package com.fenpo.android.feature.settings

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureStoreScreen
import com.fenpo.android.core.testing.demo
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

/** Google Play screenshot of Settings (design/store/android/PLAN.md). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StoreScreenshotTest {
    @Test
    fun settings() =
        captureStoreScreen("07-settings") {
            SettingsScreen(
                state = SettingsUiState(mirroredApps = 9),
                systemLanguageName = demo("Türkçe", "English"),
                onThemeModeChange = {},
                onLanguageChange = {},
                onFeatureChange = { _, _ -> },
                onOpenPermissions = {},
            )
        }
}
