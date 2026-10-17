package com.fenpo.android.core.testing

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Assume.assumeTrue
import org.robolectric.RuntimeEnvironment

/** Listing languages on Google Play (docs/android/PLAY-LISTING.md). */
private val storeLanguages = listOf("tr", "en")

/** Where the Play assets go, from a module two levels below `android/`. */
private const val STORE_FOLDER = "../../../docs/android/play"

/**
 * Draws a Google Play phone screenshot of [content] in each listing language: 1080 × 1920 px (a
 * 360 × 640 dp phone at 3×, 9:16), light theme unless [dark], into
 * `docs/android/play/<language>/<name>.png`. Only with `FENPO_PLAYSHOTS=1` and
 * `recordRoborazziDebug`; other runs skip it. Screens are filled with demo data, never anyone's
 * real names, messages or files.
 */
fun captureStoreScreenshot(
    name: String,
    dark: Boolean = false,
    content: @Composable () -> Unit,
) = captureStoreImage(name, "w360dp-h640dp-xxhdpi", dark, content)

/** Demo text in the screenshot's listing language: [tr] for the Turkish listing, else [en]. */
@Composable
fun demo(
    tr: String,
    en: String,
): String = if (LocalConfiguration.current.locales[0].language == "tr") tr else en

/** A Play asset of a fixed pixel size ([widthPx] × [heightPx], at 1×), per listing language. */
fun captureStoreAsset(
    name: String,
    widthPx: Int,
    heightPx: Int,
    content: @Composable () -> Unit,
) = captureStoreImage(name, "w${widthPx}dp-h${heightPx}dp-mdpi", dark = false, content)

private fun captureStoreImage(
    name: String,
    size: String,
    dark: Boolean,
    content: @Composable () -> Unit,
) {
    assumeTrue("Play assets are drawn only with FENPO_PLAYSHOTS=1", System.getenv("FENPO_PLAYSHOTS") == "1")
    val baseQualifiers = RuntimeEnvironment.getQualifiers()
    try {
        for (language in storeLanguages) {
            RuntimeEnvironment.setQualifiers("$language-$size")
            captureRoboImage(filePath = "$STORE_FOLDER/$language/$name.png") {
                FenpoTheme(darkTheme = dark) {
                    Surface(color = MaterialTheme.colorScheme.surface) {
                        Box(Modifier.fillMaxSize()) { content() }
                    }
                }
            }
        }
    } finally {
        RuntimeEnvironment.setQualifiers(baseQualifiers)
    }
}
