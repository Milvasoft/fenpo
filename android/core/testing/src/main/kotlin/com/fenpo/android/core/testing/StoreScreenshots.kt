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

/** Where the Play assets go, from a module two levels below `android/` (next to the Windows and iPhone ones). */
private const val SCREENSHOT_FOLDER = "../../../design/screenshots/android"
private const val ASSET_FOLDER = "../../../design/logo/store/android"

/**
 * Draws a Google Play phone screenshot of [content] in each listing language: 1080 × 1920 px (a
 * 360 × 640 dp phone at 3×, 9:16), light theme unless [dark], into
 * `design/screenshots/android/<language>/<name>.png`. Only with `FENPO_PLAYSHOTS=1` and
 * `recordRoborazziDebug`; other runs skip it. Screens are filled with demo data, never anyone's
 * real names, messages or files.
 */
fun captureStoreScreenshot(
    name: String,
    dark: Boolean = false,
    content: @Composable () -> Unit,
) = captureStoreImage("w360dp-h640dp-xxhdpi", dark, storeLanguages, { "$SCREENSHOT_FOLDER/$it/$name.png" }, content)

/** Demo text in the screenshot's listing language: [tr] for the Turkish listing, else [en]. */
@Composable
fun demo(
    tr: String,
    en: String,
): String = if (LocalConfiguration.current.locales[0].language == "tr") tr else en

/**
 * A Play asset of a fixed pixel size ([widthPx] × [heightPx], at 1×) in `design/logo/store/android/`:
 * `<name>-<language>.png` for each listing language, or one `<name>.png` when it has no text.
 */
fun captureStoreAsset(
    name: String,
    widthPx: Int,
    heightPx: Int,
    perLanguage: Boolean = true,
    content: @Composable () -> Unit,
) = captureStoreImage(
    "w${widthPx}dp-h${heightPx}dp-mdpi",
    dark = false,
    languages = if (perLanguage) storeLanguages else storeLanguages.take(1),
    path = { if (perLanguage) "$ASSET_FOLDER/$name-$it.png" else "$ASSET_FOLDER/$name.png" },
    content = content,
)

private fun captureStoreImage(
    size: String,
    dark: Boolean,
    languages: List<String>,
    path: (String) -> String,
    content: @Composable () -> Unit,
) {
    assumeTrue("Play assets are drawn only with FENPO_PLAYSHOTS=1", System.getenv("FENPO_PLAYSHOTS") == "1")
    val baseQualifiers = RuntimeEnvironment.getQualifiers()
    try {
        for (language in languages) {
            RuntimeEnvironment.setQualifiers("$language-$size")
            captureRoboImage(filePath = path(language)) {
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
