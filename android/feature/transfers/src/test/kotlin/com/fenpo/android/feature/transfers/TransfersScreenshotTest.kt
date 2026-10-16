package com.fenpo.android.feature.transfers

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.connection.transfer.ActiveTransfer
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.android.core.data.transfer.TransferRecord
import com.fenpo.android.core.testing.captureScreenVariants
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode
import java.time.Instant

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class TransfersScreenshotTest {
    @Test
    fun transfersEmpty() = captureScreenVariants("transfers_empty") { TransfersScreen() }

    @Test
    fun transfersList() =
        captureScreenVariants("transfers_list") {
            TransfersScreen(
                state =
                    TransfersUiState(
                        active =
                            listOf(
                                ActiveTransfer("a", incoming = false, "IMG_20261016_1501.jpg", 12, 64, 100),
                            ),
                        history =
                            listOf(
                                record("b", "Sözleşme_v3.pdf", incoming = true, minutesAgo = 2),
                                record("c", "IMG_20261015_1849.jpg", incoming = false, minutesAgo = 50),
                                record(
                                    "d",
                                    "sunum.pptx",
                                    incoming = false,
                                    minutesAgo = 180,
                                    outcome = TransferOutcome.FAILED,
                                ),
                            ),
                        now = NOW,
                    ),
            )
        }

    private fun record(
        id: String,
        name: String,
        incoming: Boolean,
        minutesAgo: Long,
        outcome: TransferOutcome = TransferOutcome.COMPLETED,
    ) = TransferRecord(id, name, 1, incoming, NOW.minusSeconds(minutesAgo * 60), 1_000, outcome)

    private companion object {
        val NOW: Instant = Instant.parse("2026-10-16T15:00:00Z")
    }
}
