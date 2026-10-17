package com.fenpo.android.feature.transfers

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.connection.transfer.ActiveTransfer
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.android.core.data.transfer.TransferRecord
import com.fenpo.android.core.testing.captureStoreScreenshot
import com.fenpo.android.core.testing.demo
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Instant

/** Google Play screenshot of the Transfers tab, demo data only (docs/android/PLAY-LISTING.md §2). */
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class StoreScreenshotTest {
    private val now = Instant.parse("2026-10-17T09:40:00Z")

    @Test
    fun transfers() =
        captureStoreScreenshot("03-transfers") {
            TransfersScreen(
                state =
                    TransfersUiState(
                        active = listOf(ActiveTransfer("a", incoming = false, "IMG_20261017_0912.jpg", 8, 62, 100)),
                        history =
                            listOf(
                                record(
                                    "b",
                                    demo("Sunum_taslak.pdf", "Slides_draft.pdf"),
                                    incoming = true,
                                    minutesAgo = 4,
                                ),
                                record(
                                    "c",
                                    demo("Bütçe_2027.xlsx", "Budget_2027.xlsx"),
                                    incoming = true,
                                    minutesAgo = 35,
                                ),
                                record(
                                    "d",
                                    demo("Ekran görüntüsü.png", "Screenshot.png"),
                                    incoming = false,
                                    minutesAgo = 70,
                                ),
                                record(
                                    "e",
                                    demo("Proje_planı.docx", "Project_plan.docx"),
                                    incoming = false,
                                    minutesAgo = 180,
                                ),
                            ),
                        now = now,
                    ),
            )
        }

    private fun record(
        id: String,
        name: String,
        incoming: Boolean,
        minutesAgo: Long,
    ) = TransferRecord(id, name, 1, incoming, now.minusSeconds(minutesAgo * 60), 1_000, TransferOutcome.COMPLETED)
}
