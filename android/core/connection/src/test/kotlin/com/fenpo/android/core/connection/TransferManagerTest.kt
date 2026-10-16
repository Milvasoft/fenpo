package com.fenpo.android.core.connection

import android.content.Context
import android.net.Uri
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.connection.transfer.SendResult
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.android.core.data.transfer.TransferHistory
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.android.core.data.transfer.TransferRecord
import com.fenpo.android.core.transport.TransferStream
import com.fenpo.android.core.transport.testing.TestTls
import com.fenpo.android.core.transport.tls.TlsConnector
import com.fenpo.android.core.transport.tls.sha256
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.TransferAccept
import com.fenpo.protocol.v1.TransferOffer
import com.fenpo.protocol.v1.TransferProgress
import com.fenpo.protocol.v1.TransferState
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import kotlin.random.Random
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.time.Duration.Companion.seconds

@RunWith(AndroidJUnit4::class)
class TransferManagerTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val messenger = PcMessenger()
    private val history =
        object : TransferHistory {
            override val records = MutableStateFlow<List<TransferRecord>>(emptyList())

            override suspend fun add(record: TransferRecord) {
                records.value = listOf(record) + records.value
            }
        }

    @Test
    fun sendsAFileTheWayTheProtocolSays() =
        runBlocking {
            val pc = TestTls.identity("pc")
            val phone = TestTls.identity("phone")
            val bytes = Random.nextBytes(150_000)
            val file = File(context.cacheDir, "report.pdf").apply { writeBytes(bytes) }
            val onPc = CompletableDeferred<Pair<String, ByteArray>>()
            var offer: TransferOffer? = null

            TestTls
                .startDataServer(pc) { data ->
                    val header = TransferStream.readHeader(data.source)
                    onPc.complete(header.transfer_id to data.source.readByteArray(bytes.size.toLong()))
                }.use { server ->
                    val manager = TransferManager(context, messenger, history)
                    val session =
                        PcSession(
                            LinkPath.WIFI,
                            ConnectionStatus(),
                            transport = { body, _ ->
                                // The PC: accept the offer on its control port.
                                if (body is Envelope.Body.TransferOffer) {
                                    offer = body.value
                                    val accept = TransferAccept(transfer_id = body.value.transfer_id, accepted = true)
                                    messenger.deliver(
                                        Envelope(id = 2, body = Envelope.Body.TransferAccept(accept)),
                                        LinkPath.WIFI,
                                    )
                                }
                                1L
                            },
                            onAddresses = {},
                            openData = { TlsConnector(phone).connectData(server.endpoint, pc.certificate.sha256()) },
                        )
                    session.onEnvelope(
                        Envelope(
                            id = 1,
                            body =
                                Envelope.Body.Hello(
                                    Hello(capabilities = listOf(Capability.CAPABILITY_FILE_TRANSFER)),
                                ),
                        ),
                    )
                    messenger.onSession(LinkPath.WIFI, session)
                    val handler = launch(Dispatchers.Default) { manager.run() }

                    assertEquals(SendResult.STARTED, manager.send(listOf(Uri.fromFile(file))))
                    val (id, received) = withTimeout(10.seconds) { onPc.await() }
                    // The PC confirms once every byte arrived.
                    messenger.deliver(
                        Envelope(
                            id = 3,
                            body =
                                Envelope.Body.TransferProgress(
                                    TransferProgress(
                                        transfer_id = id,
                                        bytes_done = bytes.size.toLong(),
                                        state = TransferState.TRANSFER_STATE_COMPLETED,
                                    ),
                                ),
                        ),
                        LinkPath.WIFI,
                    )
                    val record = withTimeout(10.seconds) { history.records.first { it.isNotEmpty() } }.single()
                    handler.cancel()

                    assertContentEquals(bytes, received)
                    val sentOffer = checkNotNull(offer)
                    assertEquals(id, sentOffer.transfer_id)
                    assertEquals(bytes.size.toLong(), sentOffer.total_bytes)
                    assertEquals("report.pdf", sentOffer.files.single().name)
                    assertEquals(TransferOutcome.COMPLETED, record.outcome)
                    assertEquals(false, record.incoming)
                }
        }

    @Test
    fun filesNeedWifi() =
        runBlocking {
            val manager = TransferManager(context, messenger, history)
            val session = PcSession(LinkPath.BLUETOOTH, ConnectionStatus(), { _, _ -> 1L }, {})
            session.onEnvelope(
                Envelope(
                    id = 1,
                    body = Envelope.Body.Hello(Hello(capabilities = listOf(Capability.CAPABILITY_FILE_TRANSFER))),
                ),
            )
            messenger.onSession(LinkPath.BLUETOOTH, session)

            assertEquals(SendResult.NEEDS_WIFI, manager.send(listOf(Uri.parse("content://x/1"))))
        }
}
