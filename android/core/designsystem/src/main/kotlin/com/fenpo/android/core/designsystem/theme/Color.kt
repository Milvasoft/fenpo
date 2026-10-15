package com.fenpo.android.core.designsystem.theme

import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.graphics.Color

// Material 3 roles derived from the Fenpo palette. Source of truth: the "Renk rolleri" table in
// design/android/android.html. Dynamic (wallpaper) colour is intentionally not used.

internal val LightColors =
    lightColorScheme(
        primary = Color(0xFFC93A63),
        onPrimary = Color(0xFFFFFFFF),
        primaryContainer = Color(0xFFFFD9E2),
        onPrimaryContainer = Color(0xFF3E0019),
        inversePrimary = Color(0xFFFF86A6),
        secondary = Color(0xFF7A5260),
        onSecondary = Color(0xFFFFFFFF),
        secondaryContainer = Color(0xFFF7DCE4),
        onSecondaryContainer = Color(0xFF2B151C),
        tertiary = Color(0xFF4E54C8),
        onTertiary = Color(0xFFFFFFFF),
        tertiaryContainer = Color(0xFFE0E0FF),
        onTertiaryContainer = Color(0xFF0A0E68),
        error = Color(0xFFBA1A1A),
        onError = Color(0xFFFFFFFF),
        errorContainer = Color(0xFFFFDAD6),
        onErrorContainer = Color(0xFF410002),
        background = Color(0xFFF6F7FB),
        onBackground = Color(0xFF1B1F3B),
        surface = Color(0xFFF6F7FB),
        onSurface = Color(0xFF1B1F3B),
        surfaceVariant = Color(0xFFDCE6F5),
        onSurfaceVariant = Color(0xFF5D6382),
        surfaceTint = Color(0xFFC93A63),
        inverseSurface = Color(0xFF1B1F3B),
        inverseOnSurface = Color(0xFFF6F7FB),
        outline = Color(0xFF8A8FA8),
        outlineVariant = Color(0xFFD5DAE7),
        scrim = Color(0xFF000000),
        surfaceBright = Color(0xFFF6F7FB),
        surfaceDim = Color(0xFFDCE0EA),
        surfaceContainerLowest = Color(0xFFFFFFFF),
        surfaceContainerLow = Color(0xFFF0F2F8),
        surfaceContainer = Color(0xFFEAEDF5),
        surfaceContainerHigh = Color(0xFFE4E8F2),
        surfaceContainerHighest = Color(0xFFDCE6F5),
    )

internal val DarkColors =
    darkColorScheme(
        primary = Color(0xFFFF86A6),
        onPrimary = Color(0xFF5C1130),
        primaryContainer = Color(0xFF7E2545),
        onPrimaryContainer = Color(0xFFFFD9E2),
        inversePrimary = Color(0xFFC93A63),
        secondary = Color(0xFFE3BDC8),
        onSecondary = Color(0xFF432932),
        secondaryContainer = Color(0xFF5A3341),
        onSecondaryContainer = Color(0xFFFFD9E2),
        tertiary = Color(0xFFBEC2FF),
        onTertiary = Color(0xFF1B2084),
        tertiaryContainer = Color(0xFF353BAF),
        onTertiaryContainer = Color(0xFFE0E0FF),
        error = Color(0xFFFFB4AB),
        onError = Color(0xFF690005),
        errorContainer = Color(0xFF93000A),
        onErrorContainer = Color(0xFFFFDAD6),
        background = Color(0xFF11132A),
        onBackground = Color(0xFFEEF0F8),
        surface = Color(0xFF11132A),
        onSurface = Color(0xFFEEF0F8),
        surfaceVariant = Color(0xFF2A2E4D),
        onSurfaceVariant = Color(0xFFA3A8C3),
        surfaceTint = Color(0xFFFF86A6),
        inverseSurface = Color(0xFFEEF0F8),
        inverseOnSurface = Color(0xFF1B1F3B),
        outline = Color(0xFF6F7493),
        outlineVariant = Color(0xFF2C3152),
        scrim = Color(0xFF000000),
        surfaceBright = Color(0xFF2F3352),
        surfaceDim = Color(0xFF11132A),
        surfaceContainerLowest = Color(0xFF0C0E22),
        surfaceContainerLow = Color(0xFF171A33),
        surfaceContainer = Color(0xFF1C1F3A),
        surfaceContainerHigh = Color(0xFF232744),
        surfaceContainerHighest = Color(0xFF2A2E4D),
    )

internal val LightExtendedColors =
    FenpoExtendedColors(
        success = Color(0xFF2F8F6B),
        onSuccess = Color(0xFFFFFFFF),
        successContainer = Color(0xFFC9F0DD),
        onSuccessContainer = Color(0xFF00382A),
        card = Color(0xFFFFFFFF),
        deviceFrame = Color(0xFF2A2E48),
    )

internal val DarkExtendedColors =
    FenpoExtendedColors(
        success = Color(0xFF7DD8B0),
        onSuccess = Color(0xFF00382A),
        successContainer = Color(0xFF005139),
        onSuccessContainer = Color(0xFFC9F0DD),
        card = Color(0xFF1C1F3A),
        deviceFrame = Color(0xFF6D7190),
    )
