package com.fenpo.android.core.testing

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.github.takahirom.roborazzi.ExperimentalRoborazziApi
import com.github.takahirom.roborazzi.RoborazziOptions
import com.github.takahirom.roborazzi.captureRoboImage
import org.robolectric.RuntimeEnvironment

/** Languages every screen is checked in (CLAUDE.md: "Tema ve dil her ekranda"). */
private val languages = listOf("en", "tr")

/** Small tolerance so anti-aliasing differences between machines do not fail the check. */
@OptIn(ExperimentalRoborazziApi::class)
private val options =
    RoborazziOptions(
        compareOptions = RoborazziOptions.CompareOptions(changeThreshold = 0.005f),
    )

/**
 * Captures [content] four times, light and dark in English and Turkish, into
 * `src/test/screenshots/<name>_<language>_<theme>.png`. `recordRoborazziDebug` writes the images,
 * `verifyRoborazziDebug` compares against them.
 *
 * Call from a Robolectric test with native graphics. The device size comes from the module's
 * `robolectric.properties` (a 412 x 892 dp phone, like the frames in the design).
 *
 * @param name File name stem, e.g. "home_paired".
 * @param fixedDark Draws only the dark variant for screens that are always dark (camera, scanner).
 */
fun captureScreenVariants(
    name: String,
    fixedDark: Boolean = false,
    content: @Composable () -> Unit,
) {
    val baseQualifiers = RuntimeEnvironment.getQualifiers()
    try {
        for (language in languages) {
            for (dark in if (fixedDark) listOf(true) else listOf(false, true)) {
                RuntimeEnvironment.setQualifiers("+$language")
                captureRoboImage(
                    filePath = "src/test/screenshots/${name}_${language}_${if (dark) "dark" else "light"}.png",
                    roborazziOptions = options,
                ) {
                    FenpoTheme(darkTheme = dark) {
                        Surface(color = MaterialTheme.colorScheme.surface) {
                            Box(Modifier.fillMaxSize()) { content() }
                        }
                    }
                }
            }
        }
    } finally {
        RuntimeEnvironment.setQualifiers(baseQualifiers)
    }
}
