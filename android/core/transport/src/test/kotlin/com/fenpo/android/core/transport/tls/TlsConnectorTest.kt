package com.fenpo.android.core.transport.tls

import com.fenpo.android.core.transport.Endpoint
import com.fenpo.android.core.transport.testing.TestTls
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Ping
import com.fenpo.protocol.v1.Pong
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertTrue
import kotlin.time.Duration.Companion.seconds

class TlsConnectorTest {
    private val pc = TestTls.identity("pc")
    private val phone = TestTls.identity("phone")
    private val other = TestTls.identity("other")

    @Test
    fun exchangesEnvelopesWithPinnedServer() =
        runTest {
            var clientSeenByServer = ""
            TestTls
                .startServer(pc) { connection ->
                    clientSeenByServer = connection.peerCertificateSha256.hex()
                    val ping = assertIs<Envelope.Body.Ping>(connection.receive(5.seconds)?.body)
                    connection.send(Envelope.Body.Pong(Pong(nonce = ping.value.nonce)), replyTo = 1)
                }.use { server ->
                    TlsConnector(phone).connect(server.endpoint, pc.certificate.sha256()).use { connection ->
                        assertEquals("TLSv1.3", connection.protocol)
                        assertEquals(pc.certificate.sha256(), connection.peerCertificateSha256)

                        val id = connection.send(Envelope.Body.Ping(Ping(nonce = 42)))
                        val reply = connection.receive(5.seconds)

                        assertEquals(id, reply?.reply_to)
                        assertEquals(42, assertIs<Envelope.Body.Pong>(reply?.body).value.nonce)
                    }
                    assertTrue(server.awaitFirstConnection())
                    assertEquals(phone.certificate.sha256().hex(), clientSeenByServer)
                }
        }

    @Test
    fun refusesServerWithAnotherCertificate() =
        runTest {
            TestTls.startServer(other) {}.use { server ->
                val error =
                    assertFailsWith<Exception> { TlsConnector(phone).connect(server.endpoint, pc.certificate.sha256()) }
                assertTrue(error.isPinMismatch(), error.toString())
            }
        }

    @Test
    fun refusesTls12() =
        runTest {
            TestTls.startServer(pc, protocols = arrayOf("TLSv1.2")) {}.use { server ->
                val error =
                    assertFailsWith<Exception> { TlsConnector(phone).connect(server.endpoint, pc.certificate.sha256()) }
                assertTrue(!error.isPinMismatch(), error.toString())
            }
        }

    @Test
    fun failsFastWhenNothingListens() =
        runTest {
            assertFailsWith<java.io.IOException> {
                TlsConnector(
                    phone,
                    connectTimeout = 1.seconds,
                ).connect(Endpoint("127.0.0.1", 1), pc.certificate.sha256())
            }
        }
}
