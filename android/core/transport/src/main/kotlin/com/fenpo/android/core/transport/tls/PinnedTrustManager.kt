package com.fenpo.android.core.transport.tls

import okio.ByteString
import java.net.Socket
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.SSLEngine
import javax.net.ssl.X509ExtendedTrustManager

/**
 * Trusts exactly one server certificate: the one whose SHA-256 is [pin]. There is no CA and no
 * host name check; the pin from the QR code (or from pairing) is the whole trust decision.
 */
public class PinnedTrustManager(
    private val pin: ByteString,
) : X509ExtendedTrustManager() {
    override fun checkServerTrusted(
        chain: Array<out X509Certificate>?,
        authType: String?,
    ) {
        val leaf = chain?.firstOrNull() ?: throw CertificateException("Server sent no certificate")
        val actual = leaf.sha256()
        if (!MessageDigest.isEqual(actual.toByteArray(), pin.toByteArray())) {
            throw CertificatePinMismatchException(expected = pin, actual = actual)
        }
    }

    override fun checkServerTrusted(
        chain: Array<out X509Certificate>?,
        authType: String?,
        socket: Socket?,
    ): Unit = checkServerTrusted(chain, authType)

    override fun checkServerTrusted(
        chain: Array<out X509Certificate>?,
        authType: String?,
        engine: SSLEngine?,
    ): Unit = checkServerTrusted(chain, authType)

    override fun checkClientTrusted(
        chain: Array<out X509Certificate>?,
        authType: String?,
    ): Unit = throw CertificateException("The phone is always the TLS client")

    override fun checkClientTrusted(
        chain: Array<out X509Certificate>?,
        authType: String?,
        socket: Socket?,
    ): Unit = checkClientTrusted(chain, authType)

    override fun checkClientTrusted(
        chain: Array<out X509Certificate>?,
        authType: String?,
        engine: SSLEngine?,
    ): Unit = checkClientTrusted(chain, authType)

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}

/** The server presented a different certificate than the pinned one. */
public class CertificatePinMismatchException(
    public val expected: ByteString,
    public val actual: ByteString,
) : CertificateException("Server certificate ${actual.hex()} does not match the pinned ${expected.hex()}")

/** Whether this failure, or one of its causes, is a pin mismatch. */
public fun Throwable.isPinMismatch(): Boolean =
    generateSequence(this) {
        it.cause
    }.any { it is CertificatePinMismatchException }
