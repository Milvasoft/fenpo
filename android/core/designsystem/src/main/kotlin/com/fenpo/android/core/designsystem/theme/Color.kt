package com.fenpo.android.core.designsystem.theme

import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.graphics.Color

// Material 3 roles derived from the Fenpo palette. Source of truth: the "Renk rolleri" table in
// design/android/android.html. Dynamic (wallpaper) colour is intentionally not used. The page and
// card surfaces, text and hairlines follow the iPhone app's asset catalog (Canvas, Card, TextPrimary,
// TextSecondary, TextTertiary, Hairline), so both apps look the same side by side: black and
// #1C1C22 in dark, #F2F3F8 and white in light.

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
        background = Color(0xFFF2F3F8),
        onBackground = Color(0xFF1B1F3B),
        surface = Color(0xFFF2F3F8),
        onSurface = Color(0xFF1B1F3B),
        surfaceVariant = Color(0xFFDCE6F5),
        onSurfaceVariant = Color(0xFF6B7090),
        surfaceTint = Color(0xFFC93A63),
        inverseSurface = Color(0xFF1B1F3B),
        inverseOnSurface = Color(0xFFF2F3F8),
        outline = Color(0xFF8A8FA8),
        outlineVariant = Color(0xFFE2E3EA),
        scrim = Color(0xFF000000),
        surfaceBright = Color(0xFFF2F3F8),
        surfaceDim = Color(0xFFDCE0EA),
        surfaceContainerLowest = Color(0xFFFFFFFF),
        surfaceContainerLow = Color(0xFFF7F8FB),
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
        background = Color(0xFF000000),
        onBackground = Color(0xFFF4F5FA),
        surface = Color(0xFF000000),
        onSurface = Color(0xFFF4F5FA),
        surfaceVariant = Color(0xFF303039),
        onSurfaceVariant = Color(0xFFA2A6BD),
        surfaceTint = Color(0xFFFF86A6),
        inverseSurface = Color(0xFFF4F5FA),
        inverseOnSurface = Color(0xFF1B1F3B),
        outline = Color(0xFF6D7190),
        outlineVariant = Color(0xFF333338),
        scrim = Color(0xFF000000),
        surfaceBright = Color(0xFF303039),
        surfaceDim = Color(0xFF000000),
        surfaceContainerLowest = Color(0xFF000000),
        surfaceContainerLow = Color(0xFF121216),
        surfaceContainer = Color(0xFF1C1C22),
        surfaceContainerHigh = Color(0xFF26262E),
        surfaceContainerHighest = Color(0xFF303039),
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
        card = Color(0xFF1C1C22),
        deviceFrame = Color(0xFF6D7190),
    )
