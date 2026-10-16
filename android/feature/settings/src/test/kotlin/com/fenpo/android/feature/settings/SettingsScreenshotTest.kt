package com.fenpo.android.feature.settings

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.data.notifications.NotificationApp
import com.fenpo.android.core.data.notifications.NotificationAppSettings
import com.fenpo.android.core.testing.captureScreenVariants
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class SettingsScreenshotTest {
    /** The state drawn in the design: defaults, two permissions missing. */
    private val state = SettingsUiState(missingPermissions = 2, mirroredApps = 12)

    @Test
    fun settings() =
        captureScreenVariants("settings") {
            SettingsScreen(
                state = state,
                systemLanguageName = "Türkçe",
                onThemeModeChange = {},
                onLanguageChange = {},
                onFeatureChange = { _, _ -> },
                onOpenPermissions = {},
            )
        }

    @Test
    fun languageDialog() =
        captureScreenVariants("settings_language") {
            SettingsScreen(
                state = state,
                systemLanguageName = "Türkçe",
                onThemeModeChange = {},
                onLanguageChange = {},
                onFeatureChange = { _, _ -> },
                onOpenPermissions = {},
                showLanguageDialog = true,
            )
        }

    @Test
    fun notificationApps() =
        captureScreenVariants("settings_notification_apps") {
            NotificationAppsScreen(
                settings =
                    NotificationAppSettings(
                        apps =
                            listOf(
                                NotificationApp("sms", "Mesajlar", replyable = true, enabled = true),
                                NotificationApp("wa", "WhatsApp", replyable = true, enabled = true),
                                NotificationApp("tg", "Telegram", replyable = true, enabled = true),
                                NotificationApp("cal", "Takvim", replyable = false, enabled = true),
                                NotificationApp("mail", "E-posta", replyable = false, enabled = true),
                                NotificationApp("bank", "Banka", replyable = false, enabled = false),
                            ),
                    ),
                onBack = {},
                onMirrorNewApps = {},
                onAppEnable = { _, _ -> },
            )
        }
}
