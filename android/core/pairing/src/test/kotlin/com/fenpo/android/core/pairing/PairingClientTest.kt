package com.fenpo.android.core.pairing

import com.fenpo.android.core.transport.Endpoint
import com.fenpo.android.core.transport.session.LocalDevice
import com.fenpo.android.core.transport.testing.TestTls
import com.fenpo.android.core.transport.testing.TestTlsServer
import com.fenpo.android.core.transport.tls.EnvelopeConnection
import com.fenpo.android.core.transport.tls.TlsConnector
import com.fenpo.android.core.transport.tls.sha256
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.PairingFailure
import com.fenpo.protocol.v1.PairingRequest
import com.fenpo.protocol.v1.PairingResult
import kotlinx.coroutines.test.runTest
import okio.ByteString
import okio.ByteString.Companion.toByteString
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.time.Duration.Companion.seconds

/** Pairing against an in-process TLS server that plays the PC the way the S5 spike does. */
class PairingClientTest {
    private val pc = TestTls.identity("pc")
    private val phone = TestTls.identity("phone")
    private val other = TestTls.identity("other")

    private val token = ByteArray(16) { it.toByte() }.toByteString()
    private val bleKey = ByteArray(32) { (it * 7).toByte() }.toByteString()
    private val device =
        LocalDevice(
            id = "6f1c2b9e-0d7a-4a55-9d0e-2f9a6c1e8b11",
            name = "Pixel 9",
            appVersion = "0.1.0",
            capabilities = setOf(Capability.CAPABILITY_CLIPBOARD, Capability.CAPABILITY_RING),
            protocolVersions = listOf(1),
        )
    private val client = PairingClient(TlsConnector(phone), device, resultTimeout = 5.seconds, replyTimeout = 5.seconds)

    private fun code(
        server: TestTlsServer,
        fingerprint: ByteString = pc.certificate.sha256(),
        before: List<Endpoint> = emptyList(),
    ) = PairingUri(1, "pc-7f3a", "BUGRA-PC", before + server.endpoint, fingerprint, token)

    /** Plays the PC: checks the token, answers, then exchanges `Hello`. */
    private fun pcServer(
        result: (PairingRequest) -> PairingResult = { accepted() },
        pcHello: Hello = Hello(platform = DevicePlatform.DEVICE_PLATFORM_WINDOWS, protocol_versions = listOf(1)),
        seen: MutableList<Any> = mutableListOf(),
    ): TestTlsServer =
        TestTls.startServer(pc) { connection: EnvelopeConnection ->
            seen += connection.peerCertificateSha256
            val request = assertIs<Envelope.Body.PairingRequest>(connection.receive(5.seconds)?.body).value
            seen += request
            val answer = result(request)
            connection.send(Envelope.Body.PairingResult(answer))
            if (!answer.accepted) return@startServer
            val hello = assertIs<Envelope.Body.Hello>(connection.receive(5.seconds)?.body).value
            seen += hello
            connection.send(Envelope.Body.Hello(pcHello))
        }

    private fun accepted() =
        PairingResult(
            accepted = true,
            pc_id = "pc-7f3a",
            pc_name = "BUGRA-PC",
            ble_channel_key = bleKey,
        )

    @Test
    fun pairsAndContinuesWithHello() =
        runTest {
            val seen = mutableListOf<Any>()
            val pcHello =
                Hello(
                    device_id = "pc-7f3a",
                    platform = DevicePlatform.DEVICE_PLATFORM_WINDOWS,
                    protocol_versions = listOf(1),
                    capabilities = listOf(Capability.CAPABILITY_CLIPBOARD, Capability.CAPABILITY_LOCK_PC),
                )
            pcServer(pcHello = pcHello, seen = seen).use { server ->
                val outcome = assertIs<PairingOutcome.Paired>(client.pair(code(server)))
                outcome.connection.close()

                with(outcome.pc) {
                    assertEquals("pc-7f3a", pcId)
                    assertEquals("BUGRA-PC", pcName)
                    assertEquals(pc.certificate.sha256(), certificateSha256)
                    assertEquals(bleKey, bleChannelKey)
                    assertEquals(setOf(Capability.CAPABILITY_CLIPBOARD), session.capabilities)
                    assertEquals(1, session.protocolVersion)
                }

                server.awaitFirstConnection()
                assertEquals(phone.certificate.sha256(), seen[0], "PC pins the certificate from the handshake")
                val request = seen[1] as PairingRequest
                assertEquals(token, request.token)
                assertEquals(DevicePlatform.DEVICE_PLATFORM_ANDROID, request.platform)
                assertEquals(
                    ByteString.EMPTY,
                    request.certificate_sha256,
                    "fingerprint comes from TLS, not the request",
                )
                assertEquals("Pixel 9", (seen[2] as Hello).device_name)
            }
        }

    @Test
    fun skipsUnreachableAddresses() =
        runTest {
            pcServer().use { server ->
                val outcome = client.pair(code(server, before = listOf(Endpoint("127.0.0.1", 1))))
                assertIs<PairingOutcome.Paired>(outcome).connection.close()
            }
        }

    @Test
    fun reportsUnreachableWhenNoAddressAnswers() =
        runTest {
            val code = PairingUri(1, "pc", "PC", listOf(Endpoint("127.0.0.1", 1)), pc.certificate.sha256(), token)
            assertEquals(PairingError.UNREACHABLE, assertIs<PairingOutcome.Failed>(client.pair(code)).error)
        }

    @Test
    fun stopsAtWrongCertificate() =
        runTest {
            TestTls.startServer(other) {}.use { server ->
                assertEquals(
                    PairingError.CERTIFICATE_MISMATCH,
                    assertIs<PairingOutcome.Failed>(client.pair(code(server))).error,
                )
            }
        }

    @Test
    fun mapsPcRefusals() =
        runTest {
            mapOf(
                PairingFailure.PAIRING_FAILURE_TOKEN_EXPIRED to PairingError.TOKEN_EXPIRED,
                PairingFailure.PAIRING_FAILURE_TOKEN_INVALID to PairingError.TOKEN_INVALID,
                PairingFailure.PAIRING_FAILURE_REJECTED_BY_USER to PairingError.REJECTED_BY_USER,
            ).forEach { (failure, expected) ->
                pcServer(result = { PairingResult(accepted = false, failure = failure) }).use { server ->
                    assertEquals(expected, assertIs<PairingOutcome.Failed>(client.pair(code(server))).error)
                }
            }
        }

    @Test
    fun rejectsResultForAnotherPc() =
        runTest {
            pcServer(result = { accepted().copy(pc_id = "someone-else") }).use { server ->
                assertEquals(
                    PairingError.UNEXPECTED_RESPONSE,
                    assertIs<PairingOutcome.Failed>(client.pair(code(server))).error,
                )
            }
        }

    @Test
    fun failsWithoutCommonProtocolVersion() =
        runTest {
            pcServer(pcHello = Hello(protocol_versions = listOf(2))).use { server ->
                assertEquals(
                    PairingError.PROTOCOL_MISMATCH,
                    assertIs<PairingOutcome.Failed>(client.pair(code(server))).error,
                )
            }
        }
}
