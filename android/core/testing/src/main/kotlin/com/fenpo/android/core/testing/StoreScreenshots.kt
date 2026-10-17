package com.fenpo.android.core.testing

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.layout.layout
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Assume.assumeTrue
import org.robolectric.RuntimeEnvironment
import java.io.File

// Google Play store assets (design/store/android/PLAN.md), drawn from the app's real Compose
// screens with demo data only, never anyone's names, messages or files. Only with
// FENPO_PLAYSHOTS=1 and recordRoborazziDebug (design/store/android/scripts/build.py); other test
// runs skip them.

/** Listing languages on Google Play. */
private val storeLanguages = listOf("tr", "en")

/** design/store/android, from a module two levels below `android/`. */
private const val STORE = "../../../design/store/android"

private fun requirePlayShots() =
    assumeTrue("Play assets are drawn only with FENPO_PLAYSHOTS=1", System.getenv("FENPO_PLAYSHOTS") == "1")

/**
 * One phone screenshot [id] (e.g. "01-home") in each language and theme: the plain screen with a
 * demo status bar into `raw/<language>/<theme>/<id>.png`, and the designed one (headline from
 * `metadata/<language>.md`) into `play/phone/<language>/<theme>/<id>.png`, both 1080 × 1920.
 *
 * @param alwaysDark The screen is dark in both themes (webcam mode); it is drawn dark into both.
 */
fun captureStoreScreen(
    id: String,
    alwaysDark: Boolean = false,
    content: @Composable () -> Unit,
) {
    requirePlayShots()
    for (language in storeLanguages) {
        val headline = StoreMetadata.screenshotHeadline(language, id)
        for (theme in listOf("light", "dark")) {
            val dark = alwaysDark || theme == "dark"
            capture("$STORE/raw/$language/$theme/$id.png", language, PHONE) {
                FenpoTheme(darkTheme = dark) { PhoneScreen(content) }
            }
            capture("$STORE/play/phone/$language/$theme/$id.png", language, PHONE) {
                StoreFrame(headline = headline, dark = dark, content = content)
            }
        }
    }
}

/**
 * One scene of the promo video, 1920 × 1080, into `build/video/<language>/<id>.png`: the caption
 * from `metadata/<language>.md` on the left, the screen in a phone on the right.
 */
fun captureVideoScene(
    id: String,
    alwaysDark: Boolean = false,
    content: @Composable () -> Unit,
) {
    requirePlayShots()
    for (language in storeLanguages) {
        val caption = StoreMetadata.videoCaption(language, id)
        capture("$STORE/build/video/$language/$id.png", language, LANDSCAPE) {
            BrandBackground {
                Row(
                    Modifier.fillMaxSize().padding(start = 56.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(40.dp),
                ) {
                    Text(
                        caption,
                        modifier = Modifier.weight(1f),
                        color = Color.White,
                        fontSize = 30.sp,
                        lineHeight = 38.sp,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Box(Modifier.padding(top = 36.dp).fillMaxHeight().padding(end = 64.dp)) {
                        Phone(scale = VIDEO_SCALE, dark = alwaysDark, content = content)
                    }
                }
            }
        }
    }
}

/**
 * A fixed-size Play asset (`play/<name>-<language>.png`, or one `play/<name>.png` without text),
 * at 1 px per dp.
 */
fun captureStoreAsset(
    name: String,
    widthPx: Int,
    heightPx: Int,
    perLanguage: Boolean = true,
    content: @Composable () -> Unit,
) {
    requirePlayShots()
    for (language in if (perLanguage) storeLanguages else storeLanguages.take(1)) {
        val file = if (perLanguage) "$STORE/play/$name-$language.png" else "$STORE/play/$name.png"
        capture(file, language, "w${widthPx}dp-h${heightPx}dp-mdpi") {
            FenpoTheme(darkTheme = false) { Box(Modifier.fillMaxSize()) { content() } }
        }
    }
}

/** A frame for the video at 1920 × 1080 (`build/video/<language>/<id>.png`), e.g. the end card. */
fun captureVideoCard(
    id: String,
    content: @Composable (language: String) -> Unit,
) {
    requirePlayShots()
    for (language in storeLanguages) {
        capture("$STORE/build/video/$language/$id.png", language, LANDSCAPE) {
            FenpoTheme(darkTheme = false) { Box(Modifier.fillMaxSize()) { content(language) } }
        }
    }
}

/** Demo text in the asset's listing language: [tr] for the Turkish listing, else [en]. */
@Composable
fun demo(
    tr: String,
    en: String,
): String = if (LocalConfiguration.current.locales[0].language == "tr") tr else en

/** The text of `metadata/<language>.md`, the same file the build script checks. */
object StoreMetadata {
    private val headlineSections = setOf("Ekran görüntüsü başlıkları", "Screenshot headlines")
    private val captionSections = setOf("Video altyazıları", "Video captions")
    private val textSections = setOf("Öne çıkan grafik metni", "Feature graphic text")

    fun screenshotHeadline(
        language: String,
        id: String,
    ): String = entry(language, headlineSections, id)

    fun videoCaption(
        language: String,
        id: String,
    ): String = entry(language, captionSections, id)

    fun featureGraphicText(language: String): String = section(language, textSections).joinToString(" ").trim()

    private fun entry(
        language: String,
        sections: Set<String>,
        id: String,
    ): String =
        section(language, sections)
            .firstOrNull { it.startsWith("- $id:") }
            ?.substringAfter(":")
            ?.trim()
            ?: error("No \"$id\" in metadata/$language.md")

    private fun section(
        language: String,
        names: Set<String>,
    ): List<String> {
        val lines = File("$STORE/metadata/$language.md").readLines()
        val start = lines.indexOfFirst { it.startsWith("## ") && it.removePrefix("## ").trim() in names }
        check(start >= 0) { "metadata/$language.md has none of $names" }
        return lines.drop(start + 1).takeWhile { !it.startsWith("## ") }.filter { it.isNotBlank() }
    }
}

/**
 * A designed Play screenshot: the [headline] in the top fifth on Fenpo's brand background, then
 * the app's own screen at 80 % inside a phone. The background is the same in both themes; only
 * the screen follows [dark].
 */
@Composable
fun StoreFrame(
    headline: String,
    dark: Boolean,
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    BrandBackground(modifier) {
        Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally) {
            Box(Modifier.fillMaxWidth().height(HEADLINE_HEIGHT), contentAlignment = Alignment.Center) {
                Text(
                    headline,
                    modifier = Modifier.padding(horizontal = 24.dp),
                    color = Color.White,
                    fontSize = 23.sp,
                    lineHeight = 29.sp,
                    fontWeight = FontWeight.SemiBold,
                    textAlign = TextAlign.Center,
                )
            }
            Phone(scale = FRAME_SCALE, dark = dark, content = content)
        }
    }
}

/** Fenpo's store background: Mürekkep into Alacakaranlık with a soft Çiçek glow at the top right. */
@Composable
fun BrandBackground(
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit,
) {
    Box(modifier.fillMaxSize().background(Brush.linearGradient(listOf(INK, NIGHT, DUSK)))) {
        Box(
            Modifier.fillMaxSize().background(
                Brush.radialGradient(
                    listOf(BLOOM.copy(alpha = 0.45f), Color.Transparent),
                    center = Offset(980f, 260f),
                    radius = 900f,
                ),
            ),
        )
        Box(
            Modifier.fillMaxSize().background(
                Brush.radialGradient(
                    listOf(DUSK.copy(alpha = 0.55f), Color.Transparent),
                    center = Offset(80f, 1800f),
                    radius = 1000f,
                ),
            ),
        )
        content()
    }
}

/** The app's screen at [scale] in a dark phone body, cut off at the bottom edge. */
@Composable
private fun Phone(
    scale: Float,
    dark: Boolean,
    content: @Composable () -> Unit,
) {
    val body = RoundedCornerShape(topStart = 38.dp * scale / FRAME_SCALE, topEnd = 38.dp * scale / FRAME_SCALE)
    Box(
        Modifier
            .width(SCREEN_WIDTH * scale + BEZEL * 2)
            .fillMaxHeight()
            .shadow(elevation = 28.dp, shape = body, ambientColor = Color.Black, spotColor = Color.Black)
            .clip(body)
            .background(BEZEL_COLOR)
            .padding(start = BEZEL, end = BEZEL, top = BEZEL),
    ) {
        FenpoTheme(darkTheme = dark) {
            val colors = MaterialTheme.colorScheme
            Box(
                Modifier
                    .fillMaxSize()
                    .clip(
                        RoundedCornerShape(
                            topStart = 32.dp * scale / FRAME_SCALE,
                            topEnd =
                                32.dp * scale / FRAME_SCALE,
                        ),
                    ).background(colors.surface),
            ) {
                // The screen is laid out at its real phone size, then drawn smaller.
                Box(Modifier.scaledScreen(scale)) { PhoneScreen(content) }
            }
        }
    }
}

/** The screen with a status bar as in demo mode, on the theme's surface. */
@Composable
private fun PhoneScreen(content: @Composable () -> Unit) {
    Surface(color = MaterialTheme.colorScheme.surface) {
        Column {
            DemoStatusBar()
            Box(Modifier.fillMaxSize()) { content() }
        }
    }
}

private fun Modifier.scaledScreen(scale: Float) =
    layout { measurable, _ ->
        val screen = measurable.measure(Constraints.fixed(SCREEN_WIDTH.roundToPx(), SCREEN_HEIGHT.roundToPx()))
        layout((screen.width * scale).toInt(), (screen.height * scale).toInt()) {
            screen.placeWithLayer(0, 0) {
                scaleX = scale
                scaleY = scale
                transformOrigin = TransformOrigin(0f, 0f)
            }
        }
    }

/** The status bar of a phone in demo mode: 12:00, Wi-Fi, full battery, no notifications. */
@Composable
private fun DemoStatusBar() {
    val color = MaterialTheme.colorScheme.onSurface
    Row(
        modifier = Modifier.fillMaxWidth().height(28.dp).padding(horizontal = 20.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text("12:00", color = color, fontSize = 13.sp, fontWeight = FontWeight.Medium)
        Spacer(Modifier.weight(1f))
        Icon(painterResource(FenpoIcons.Wifi), contentDescription = null, tint = color, modifier = Modifier.size(15.dp))
        Spacer(Modifier.width(6.dp))
        Icon(
            painterResource(FenpoIcons.Battery),
            contentDescription = null,
            tint = color,
            modifier = Modifier.size(15.dp),
        )
    }
}

private fun capture(
    path: String,
    language: String,
    size: String,
    content: @Composable () -> Unit,
) {
    val baseQualifiers = RuntimeEnvironment.getQualifiers()
    try {
        RuntimeEnvironment.setQualifiers("$language-$size")
        captureRoboImage(filePath = path) { content() }
    } finally {
        RuntimeEnvironment.setQualifiers(baseQualifiers)
    }
}

/** 1080 × 1920: a 360 × 640 dp phone at 3×. */
private const val PHONE = "w360dp-h640dp-xxhdpi"

/** 1920 × 1080: 640 × 360 dp at 3×. */
private const val LANDSCAPE = "w640dp-h360dp-xxhdpi"

private val HEADLINE_HEIGHT = 112.dp
private val SCREEN_WIDTH = 360.dp
private val SCREEN_HEIGHT = 640.dp
private val BEZEL: Dp = 7.dp
private const val FRAME_SCALE = 0.8f
private const val VIDEO_SCALE = 0.5f

// Brand colours for marketing art only (CLAUDE.md: Mürekkep, Alacakaranlık, Çiçek); the app takes colours from the theme.
private val INK = Color(0xFF1B1F3B)
private val NIGHT = Color(0xFF2A2E6B)
private val DUSK = Color(0xFF4E54C8)
private val BLOOM = Color(0xFFE8577E)
private val BEZEL_COLOR = Color(0xFF0B0C16)
