package com.fenpo.android.core.transport.tls

import okio.ByteString
import okio.ByteString.Companion.toByteString
import java.security.cert.X509Certificate
import javax.net.ssl.X509KeyManager

/**
 * This device's TLS identity: a self-signed ECDSA P-256 certificate and its key (DECISIONS #12).
 * On the phone the key lives in the Android Keystore and never leaves it.
 */
public interface TlsIdentity {
    /** Presents [certificate] as the client certificate during the handshake. */
    public val keyManager: X509KeyManager

    /** The certificate the PC pins after pairing. */
    public val certificate: X509Certificate
}

/** SHA-256 of the DER encoded certificate: the fingerprint in the QR code (`fp`) and the pin. */
public fun X509Certificate.sha256(): ByteString = encoded.toByteString().sha256()
