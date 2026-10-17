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
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.testing.captureStoreAsset
import com.fenpo.android.core.testing.captureStoreScreenshot
import com.fenpo.android.core.testing.demo
import com.fenpo.protocol.v1.MediaState
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Duration
import java.time.Instant

/**
 * Google Play assets (docs/android/PLAY-LISTING.md §2): the home screen, the PC tab, the icon and
 * the feature graphic, with demo data only. `FENPO_PLAYSHOTS=1 ./gradlew recordRoborazziDebug`.
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

    @Test
    fun home() =
        captureStoreScreenshot("01-home") {
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
        }

    @Test
    fun pcTab() =
        captureStoreScreenshot("02-pc") {
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
        }

    /** 512 × 512, full square; Play rounds the corners itself. */
    @Test
    fun icon() =
        captureStoreAsset("icon-512x512", 512, 512, perLanguage = false) {
            Box(Modifier.fillMaxSize().background(BLOOM)) {
                Image(painterResource(FenpoIcons.Logo), contentDescription = null, modifier = Modifier.fillMaxSize())
            }
        }

    /** 1024 × 500: logo, name and one line on Mürekkep. */
    @Test
    fun featureGraphic() =
        captureStoreAsset("feature-graphic-1024x500", 1024, 500) {
            val turkish = LocalConfiguration.current.locales[0].language == "tr"
            Row(
                modifier = Modifier.fillMaxSize().background(INK).padding(horizontal = 88.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(48.dp),
            ) {
                Image(painterResource(FenpoIcons.Logo), contentDescription = null, modifier = Modifier.size(220.dp))
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Text("Fenpo", color = Color.White, fontSize = 88.sp, fontWeight = FontWeight.SemiBold)
                    Text(
                        if (turkish) "Telefonun ve PC’n, tek cihaz gibi" else "Your phone and PC, working as one",
                        color = GLASS,
                        fontSize = 34.sp,
                    )
                }
            }
        }

    private companion object {
        // Brand colours for marketing art only (CLAUDE.md: Çiçek, Mürekkep, Cam); the app takes colours from the theme.
        val BLOOM = Color(0xFFE8577E)
        val INK = Color(0xFF1B1F3B)
        val GLASS = Color(0xFFDCE6F5)
    }
}
