package com.fenpo.android.core.transport.tls

import com.fenpo.android.core.transport.Endpoint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okio.ByteString
import java.net.InetSocketAddress
import java.net.Socket
import java.security.SecureRandom
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/**
 * Opens the phone's side of the data channel (④): TLS 1.3 only, this device's certificate as the
 * client certificate, and the PC's certificate pinned by its SHA-256 (docs/ARCHITECTURE.md,
 * "Eşleşme ve güvenlik").
 */
public class TlsConnector(
    private val identity: TlsIdentity,
    private val connectTimeout: Duration = 5.seconds,
    private val handshakeTimeout: Duration = 10.seconds,
) {
    /**
     * Connects to [endpoint] and completes the handshake.
     *
     * @param pinnedCertificateSha256 Expected SHA-256 of the PC certificate.
     * @throws java.io.IOException on network failures; [Throwable.isPinMismatch] tells a wrong
     *   certificate apart.
     */
    public suspend fun connect(
        endpoint: Endpoint,
        pinnedCertificateSha256: ByteString,
    ): EnvelopeConnection =
        withContext(Dispatchers.IO) {
            val context = SSLContext.getInstance(TLS_1_3)
            context.init(
                arrayOf(identity.keyManager),
                arrayOf(PinnedTrustManager(pinnedCertificateSha256)),
                SecureRandom(),
            )

            val raw = Socket()
            try {
                raw.tcpNoDelay = true
                raw.connect(InetSocketAddress(endpoint.host, endpoint.port), connectTimeout.inWholeMilliseconds.toInt())
                val socket = context.socketFactory.createSocket(raw, endpoint.host, endpoint.port, true) as SSLSocket
                socket.enabledProtocols = arrayOf(TLS_1_3)
                socket.useClientMode = true
                socket.soTimeout = handshakeTimeout.inWholeMilliseconds.toInt()
                socket.startHandshake()
                EnvelopeConnection(socket)
            } catch (e: Exception) {
                runCatching { raw.close() }
                throw e
            }
        }

    private companion object {
        const val TLS_1_3 = "TLSv1.3"
    }
}
