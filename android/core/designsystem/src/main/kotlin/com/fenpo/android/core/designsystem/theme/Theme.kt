package com.fenpo.android.core.designsystem.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color

/**
 * Fenpo colours that Material 3 has no role for: the success (Yaprak) roles used for the
 * "connected" state, the card colour (white in light, a raised surface in dark) and the frame
 * of the laptop illustration on the PC card.
 */
@Immutable
data class FenpoExtendedColors(
    val success: Color,
    val onSuccess: Color,
    val successContainer: Color,
    val onSuccessContainer: Color,
    val card: Color,
    val deviceFrame: Color,
)

val LocalFenpoExtendedColors = staticCompositionLocalOf { LightExtendedColors }

/**
 * Applies the Fenpo colour scheme. Fenpo never uses dynamic colour, so every screen looks the
 * same as the design on every phone.
 *
 * @param darkTheme Whether to use the dark scheme; follows the system unless the user chose
 *   Light or Dark in Settings.
 * @param content The content to theme.
 */
@Composable
fun FenpoTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    CompositionLocalProvider(
        LocalFenpoExtendedColors provides if (darkTheme) DarkExtendedColors else LightExtendedColors,
    ) {
        MaterialTheme(
            colorScheme = if (darkTheme) DarkColors else LightColors,
            content = content,
        )
    }
}

/** Access to Fenpo's extended colour roles inside [FenpoTheme]. */
object FenpoTheme {
    /** Success and card colours for the current theme. */
    val extendedColors: FenpoExtendedColors
        @Composable
        @ReadOnlyComposable
        get() = LocalFenpoExtendedColors.current
}
