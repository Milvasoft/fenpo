package com.fenpo.android.core.transport

import com.fenpo.android.core.transport.testing.TestTls
import com.fenpo.android.core.transport.tls.TlsConnector
import com.fenpo.android.core.transport.tls.sha256
import com.fenpo.protocol.v1.TransferStreamHeader
import kotlinx.coroutines.test.runTest
import okio.Buffer
import okio.EOFException
import okio.blackholeSink
import kotlin.random.Random
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class TransferStreamTest {
    @Test
    fun headerRoundTripsWithItsLengthPrefix() {
        val buffer = Buffer()
        TransferStream.writeHeader(buffer, TransferStreamHeader(transfer_id = "t-1"))

        assertEquals(
            TransferStreamHeader.ADAPTER.encodedSize(TransferStreamHeader(transfer_id = "t-1")),
            buffer.readInt(),
        )
        TransferStream.writeHeader(buffer.also { it.clear() }, TransferStreamHeader(transfer_id = "t-1"))
        assertEquals("t-1", TransferStream.readHeader(buffer).transfer_id)
    }

    @Test
    fun copiesExactlyTheFileSizeAndLeavesTheNextFile() {
        val source = Buffer().write(ByteArray(200_000) { 1 }).write(ByteArray(10) { 2 })
        val first = Buffer()
        val counts = mutableListOf<Long>()

        TransferStream.copyExactly(source, first, 200_000) { counts += it }

        assertEquals(200_000, first.size)
        assertEquals(200_000L, counts.last())
        assertTrue(counts.size > 1, "progress is reported per chunk")
        assertEquals(10, source.size)
    }

    @Test
    fun failsWhenTheStreamEndsEarly() {
        assertFailsWith<EOFException> {
            TransferStream.copyExactly(Buffer().write(ByteArray(5)), blackholeSink(), 6)
        }
    }

    @Test
    fun carriesFilesOverPinnedTls() =
        runTest {
            val pc = TestTls.identity("pc")
            val phone = TestTls.identity("phone")
            val files = listOf(Random.nextBytes(70_000), Random.nextBytes(3))
            var header = ""
            TestTls
                .startDataServer(pc) { connection ->
                    // The PC as the sender: read the phone's header, then write both files.
                    header = TransferStream.readHeader(connection.source).transfer_id
                    files.forEach { connection.sink.write(it) }
                    connection.sink.flush()
                }.use { server ->
                    TlsConnector(phone).connectData(server.endpoint, pc.certificate.sha256()).use { connection ->
                        TransferStream.writeHeader(connection.sink, TransferStreamHeader(transfer_id = "t-2"))
                        val received =
                            files.map { file ->
                                Buffer().also { TransferStream.copyExactly(connection.source, it, file.size.toLong()) }
                            }
                        received.zip(files).forEach { (got, want) -> assertContentEquals(want, got.readByteArray()) }
                    }
                    assertTrue(server.awaitFirstConnection())
                    assertEquals("t-2", header)
                }
        }
}
