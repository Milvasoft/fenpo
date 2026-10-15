package com.fenpo.android.core.data.settings

/** Appearance choice in Settings. */
enum class ThemeMode {
    /** Follow the system's light or dark setting. */
    SYSTEM,
    LIGHT,
    DARK,
}

/** Feature switches in Settings > Features. All default to on except screenshots. */
data class FeatureSettings(
    val notificationMirroring: Boolean = true,
    val messageSync: Boolean = true,
    val clipboardSharing: Boolean = true,
    val screenshotSharing: Boolean = false,
    val doNotDisturbSync: Boolean = true,
)

/** Everything the user chose in Settings, except the language (stored by the system, see [AppLanguage]). */
data class UserSettings(
    val themeMode: ThemeMode = ThemeMode.SYSTEM,
    val features: FeatureSettings = FeatureSettings(),
)

/** A switch in [FeatureSettings]. */
enum class Feature {
    NOTIFICATION_MIRRORING,
    MESSAGE_SYNC,
    CLIPBOARD_SHARING,
    SCREENSHOT_SHARING,
    DO_NOT_DISTURB_SYNC,
}
