package com.fenpo.android.feature.home

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureScreenVariants
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Duration
import java.time.Instant

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class HomeScreenshotTest {
    private val now = Instant.parse("2026-10-15T15:51:00Z")

    /** The state drawn in the design ("Ana ekran"). */
    private val paired =
        HomeUiState(
            pc =
                PcCardState(
                    name = "BUGRA-PC",
                    osLabel = "Windows 11",
                    connected = true,
                    bluetooth = true,
                    wifi = true,
                ),
            phoneClipboardPreview = "Doğrulama kodun",
            pcClipboardPreview = "docker compose -f deploy.yml up -d",
            recentPhotoCount = 3,
            mirroring = MirroringSummary(appCount = 12, lastSender = "Mert", lastAt = now.minus(Duration.ofMinutes(2))),
            transfers =
                listOf(
                    TransferItem("1", "Sözleşme_v3.pdf", incoming = true, at = now.minus(Duration.ofMinutes(2))),
                    TransferItem(
                        "2",
                        "IMG_20261015_1849.jpg",
                        incoming = false,
                        at = now.minus(Duration.ofMinutes(5)),
                        folder = "Resimler",
                    ),
                ),
            now = now,
        )

    @Test
    fun homePaired() =
        captureScreenVariants("home_paired") {
            HomeScreen(state = paired, onPair = {}, onOpenNotificationSettings = {})
        }

    @Test
    fun homeNotPaired() =
        captureScreenVariants("home_not_paired") {
            HomeScreen(state = HomeUiState(now = now), onPair = {}, onOpenNotificationSettings = {})
        }
}
