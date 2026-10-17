package com.fenpo.android.feature.home

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.testing.BrandBackground
import com.fenpo.android.core.testing.StoreMetadata
import com.fenpo.android.core.testing.captureStoreAsset
import com.fenpo.android.core.testing.captureStoreScreen
import com.fenpo.android.core.testing.captureVideoCard
import com.fenpo.android.core.testing.captureVideoScene
import com.fenpo.android.core.testing.demo
import com.fenpo.protocol.v1.MediaState
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Duration
import java.time.Instant

/**
 * Google Play assets (design/store/android/PLAN.md): the home screen, the PC tab, the icon, the
 * feature graphic and the video's end card, with demo data only.
 */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StoreScreenshotTest {
    private val now = Instant.parse("2026-10-17T09:40:00Z")

    @Composable
    private fun pc() =
        PcCardState(
            name = demo("Ofis PC", "Office PC"),
            osLabel = "Windows 11",
            connected = true,
            bluetooth = true,
            wifi = true,
        )

    @Composable
    private fun Home() =
        HomeScreen(
            state =
                HomeUiState(
                    pc = pc(),
                    phoneClipboardPreview = demo("Toplantı notları", "Meeting notes"),
                    pcClipboardPreview = demo("Toplantı saat 14.00’te", "Meeting at 2 pm"),
                    recentPhotoCount = 4,
                    mirroring =
                        MirroringSummary(
                            appCount = 9,
                            lastSender = demo("Ayşe", "Emma"),
                            lastAt = now.minus(Duration.ofMinutes(3)),
                        ),
                    transfers =
                        listOf(
                            TransferItem(
                                "1",
                                demo("Sunum_taslak.pdf", "Slides_draft.pdf"),
                                incoming = true,
                                at = now.minus(Duration.ofMinutes(4)),
                            ),
                            TransferItem(
                                "2",
                                "IMG_20261017_0912.jpg",
                                incoming = false,
                                at = now.minus(Duration.ofMinutes(20)),
                            ),
                        ),
                    now = now,
                ),
            onPair = {},
            onOpenNotificationSettings = {},
        )

    @Composable
    private fun Pc() =
        PcScreen(
            pc = pc(),
            onPair = {},
            tab =
                PcTabState(
                    media =
                        MediaState(
                            active = true,
                            app_name = demo("Müzik", "Music"),
                            title = demo("Sabah Kahvesi", "Morning Coffee"),
                            artist = demo("Demo Ses", "Demo Sound"),
                            playing = true,
                            can_next = true,
                            can_previous = true,
                            volume_percent = 40,
                        ),
                    canRing = true,
                    canLock = true,
                ),
        )

    @Test
    fun home() = captureStoreScreen("01-home") { Home() }

    @Test
    fun pcTab() = captureStoreScreen("02-pc") { Pc() }

    @Test
    fun homeScene() = captureVideoScene("01-home") { Home() }

    @Test
    fun pcScene() = captureVideoScene("02-pc") { Pc() }

    /** 512 × 512, full square and opaque; Play rounds the corners itself. */
    @Test
    fun icon() =
        captureStoreAsset("icon-512", 512, 512, perLanguage = false) {
            Box(Modifier.fillMaxSize().background(BLOOM)) {
                Image(painterResource(FenpoIcons.Logo), contentDescription = null, modifier = Modifier.fillMaxSize())
            }
        }

    /** 1024 × 500: logo, name and one line on the store background. */
    @Test
    fun featureGraphic() =
        captureStoreAsset("feature-graphic", 1024, 500) {
            BrandBackground {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.CenterStart) {
                    Brand(
                        text = demo(StoreMetadata.featureGraphicText("tr"), StoreMetadata.featureGraphicText("en")),
                        logo = 220,
                        name = 88,
                        line = 34,
                    )
                }
            }
        }

    /** The last scene of the promo video. */
    @Test
    fun endCard() =
        captureVideoCard("end") { language ->
            BrandBackground {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Brand(
                        text = StoreMetadata.videoCaption(language, "end"),
                        logo = 120,
                        name = 52,
                        line = 20,
                        padding = 0,
                    )
                }
            }
        }

    @Composable
    private fun Brand(
        text: String,
        logo: Int,
        name: Int,
        line: Int,
        padding: Int = 88,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = padding.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy((logo * 0.22f).dp),
        ) {
            Image(painterResource(FenpoIcons.Logo), contentDescription = null, modifier = Modifier.size(logo.dp))
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text("Fenpo", color = Color.White, fontSize = name.sp, fontWeight = FontWeight.SemiBold)
                Text(text, color = GLASS, fontSize = line.sp)
            }
        }
    }

    private companion object {
        // Brand colours for marketing art only (CLAUDE.md: Çiçek, Cam); the app takes colours from the theme.
        val BLOOM = Color(0xFFE8577E)
        val GLASS = Color(0xFFDCE6F5)
    }
}
