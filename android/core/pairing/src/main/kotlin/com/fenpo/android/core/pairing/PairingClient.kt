package com.fenpo.android.core.pairing

import com.fenpo.android.core.transport.Endpoint
import com.fenpo.android.core.transport.session.HelloNegotiation
import com.fenpo.android.core.transport.session.LocalDevice
import com.fenpo.android.core.transport.session.NegotiatedSession
import com.fenpo.android.core.transport.tls.EnvelopeConnection
import com.fenpo.android.core.transport.tls.TlsConnector
import com.fenpo.android.core.transport.tls.isPinMismatch
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.PairingFailure
import com.fenpo.protocol.v1.PairingRequest
import kotlinx.coroutines.CancellationException
import okio.ByteString
import java.io.IOException
import java.net.SocketTimeoutException
import javax.net.ssl.SSLException
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/**
 * What the phone learns about the PC by pairing; stored and used for every later connection.
 *
 * @property certificateSha256 Pin for the PC's TLS certificate (from the QR code, confirmed by the handshake).
 * @property bleChannelKey 32 byte AES-GCM key for the Fenpo BLE channel.
 */
public data class PairedPcInfo(
    val pcId: String,
    val pcName: String,
    val certificateSha256: ByteString,
    val addresses: List<Endpoint>,
    val bleChannelKey: ByteString,
    val session: NegotiatedSession,
)

/** Outcome of [PairingClient.pair]. */
public sealed interface PairingOutcome {
    /**
     * Paired. [connection] is the open control connection after the `Hello` exchange; the caller
     * keeps or closes it.
     */
    public data class Paired(
        val pc: PairedPcInfo,
        val connection: EnvelopeConnection,
    ) : PairingOutcome

    /** Not paired. [cause] is kept for logs only. */
    public data class Failed(
        val error: PairingError,
        val cause: Throwable? = null,
    ) : PairingOutcome
}

/** Why pairing failed, each with a distinct message and fix in the UI. */
public enum class PairingError {
    /** None of the addresses in the code answered: different network, PC asleep or firewall. */
    UNREACHABLE,

    /** The PC's certificate is not the one in the code: the code belongs to another PC or the connection is intercepted. */
    CERTIFICATE_MISMATCH,

    /** TLS failed for another reason. */
    HANDSHAKE_FAILED,

    /** The code expired on the PC; show a new one. */
    TOKEN_EXPIRED,

    /** The PC does not know the token, e.g. it was already used. */
    TOKEN_INVALID,

    /** Someone declined the pairing on the PC. */
    REJECTED_BY_USER,

    /** No common protocol version; one side needs an update. */
    PROTOCOL_MISMATCH,

    /** The PC took too long to answer. */
    TIMED_OUT,

    /** The PC answered something that does not follow the protocol, or closed the connection. */
    UNEXPECTED_RESPONSE,
}

/**
 * Pairs with a PC from its QR code (docs/ARCHITECTURE.md, "Eşleşme ve güvenlik"; S5):
 * TLS with the code's fingerprint pinned and this phone's certificate → `PairingRequest` with the
 * one-time token → `PairingResult` → `Hello` both ways on the same connection.
 *
 * The phone's certificate fingerprint is not in the request: the PC takes it from the TLS
 * handshake, which is the only value that proves anything (pairing.proto).
 *
 * @param resultTimeout How long to wait for `PairingResult`; the PC may ask its user first.
 */
public class PairingClient(
    private val connector: TlsConnector,
    private val device: LocalDevice,
    private val resultTimeout: Duration = 90.seconds,
    private val replyTimeout: Duration = 10.seconds,
) {
    /** Runs the pairing. Never throws except for cancellation. */
    public suspend fun pair(code: PairingUri): PairingOutcome {
        val connection =
            when (val opened = open(code)) {
                is Opened.Connected -> opened.connection
                is Opened.Failed -> return opened.outcome
            }
        return try {
            exchange(code, connection).also { if (it is PairingOutcome.Failed) connection.close() }
        } catch (e: CancellationException) {
            connection.close()
            throw e
        } catch (e: SocketTimeoutException) {
            connection.close()
            PairingOutcome.Failed(PairingError.TIMED_OUT, e)
        } catch (e: IOException) {
            connection.close()
            PairingOutcome.Failed(PairingError.UNEXPECTED_RESPONSE, e)
        }
    }

    private sealed interface Opened {
        data class Connected(
            val connection: EnvelopeConnection,
        ) : Opened

        data class Failed(
            val outcome: PairingOutcome.Failed,
        ) : Opened
    }

    /** Tries the addresses in the PC's order. A wrong certificate stops at once: it is not a network problem. */
    private suspend fun open(code: PairingUri): Opened {
        var lastError: Throwable? = null
        var handshakeFailed = false
        for (endpoint in code.addresses) {
            try {
                return Opened.Connected(connector.connect(endpoint, code.certificateSha256))
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                if (e.isPinMismatch()) return Opened.Failed(PairingOutcome.Failed(PairingError.CERTIFICATE_MISMATCH, e))
                handshakeFailed = handshakeFailed || e is SSLException
                lastError = e
            }
        }
        val error = if (handshakeFailed) PairingError.HANDSHAKE_FAILED else PairingError.UNREACHABLE
        return Opened.Failed(PairingOutcome.Failed(error, lastError))
    }

    private suspend fun exchange(
        code: PairingUri,
        connection: EnvelopeConnection,
    ): PairingOutcome {
        val requestId =
            connection.send(
                Envelope.Body.PairingRequest(
                    PairingRequest(
                        token = code.token,
                        device_id = device.id,
                        device_name = device.name,
                        platform = DevicePlatform.DEVICE_PLATFORM_ANDROID,
                    ),
                ),
            )

        val result =
            when (val body = connection.receive(resultTimeout)?.body) {
                is Envelope.Body.PairingResult -> body.value

                null -> return PairingOutcome.Failed(PairingError.UNEXPECTED_RESPONSE)

                else -> return PairingOutcome.Failed(
                    PairingError.UNEXPECTED_RESPONSE,
                    IllegalStateException("Got $body for request $requestId"),
                )
            }
        if (!result.accepted) return PairingOutcome.Failed(result.failure.toError())
        if (result.pc_id != code.pcId || result.ble_channel_key.size != BLE_KEY_BYTES) {
            return PairingOutcome.Failed(
                PairingError.UNEXPECTED_RESPONSE,
                IllegalStateException("Invalid PairingResult for ${result.pc_id}"),
            )
        }

        val localHello = device.hello()
        connection.send(Envelope.Body.Hello(localHello))
        val remoteHello =
            when (val body = connection.receive(replyTimeout)?.body) {
                is Envelope.Body.Hello -> body.value
                else -> return PairingOutcome.Failed(PairingError.UNEXPECTED_RESPONSE)
            }
        val session =
            HelloNegotiation.negotiate(localHello, remoteHello)
                ?: return PairingOutcome.Failed(PairingError.PROTOCOL_MISMATCH)

        val pc =
            PairedPcInfo(
                pcId = result.pc_id,
                pcName = result.pc_name.ifBlank { code.pcName },
                certificateSha256 = connection.peerCertificateSha256,
                addresses = code.addresses,
                bleChannelKey = result.ble_channel_key,
                session = session,
            )
        return PairingOutcome.Paired(pc, connection)
    }

    private fun PairingFailure.toError(): PairingError =
        when (this) {
            PairingFailure.PAIRING_FAILURE_TOKEN_EXPIRED -> PairingError.TOKEN_EXPIRED
            PairingFailure.PAIRING_FAILURE_TOKEN_INVALID -> PairingError.TOKEN_INVALID
            PairingFailure.PAIRING_FAILURE_REJECTED_BY_USER -> PairingError.REJECTED_BY_USER
            PairingFailure.PAIRING_FAILURE_PROTOCOL_MISMATCH -> PairingError.PROTOCOL_MISMATCH
            PairingFailure.PAIRING_FAILURE_UNSPECIFIED -> PairingError.UNEXPECTED_RESPONSE
        }

    private companion object {
        const val BLE_KEY_BYTES = 32
    }
}
