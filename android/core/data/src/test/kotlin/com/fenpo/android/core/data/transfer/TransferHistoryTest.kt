package com.fenpo.android.core.data.transfer

import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Test
import org.junit.runner.RunWith
import java.time.Instant
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
class TransferHistoryTest {
    @Test
    fun roundTripsAndDropsBrokenEntries() {
        val records =
            listOf(
                TransferRecord(
                    "a",
                    "Sözleşme \"v3\".pdf",
                    1,
                    true,
                    Instant.ofEpochMilli(1_000),
                    42,
                    TransferOutcome.COMPLETED,
                    "content://m/1",
                ),
                TransferRecord("b", "IMG.jpg", 12, false, Instant.ofEpochMilli(2_000), 9, TransferOutcome.FAILED),
            )
        val text = DataStoreTransferHistory.encode(records)

        assertEquals(records, DataStoreTransferHistory.decode(text))
        assertEquals(records.take(1), DataStoreTransferHistory.decode(text.replace("\"FAILED\"", "\"EXPLODED\"")))
        assertEquals(emptyList(), DataStoreTransferHistory.decode("not json"))
    }
}
