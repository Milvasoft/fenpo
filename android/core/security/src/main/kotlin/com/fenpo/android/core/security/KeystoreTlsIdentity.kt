package com.fenpo.android.core.security

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import com.fenpo.android.core.transport.tls.TlsIdentity
import java.math.BigInteger
import java.net.Socket
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.Principal
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.security.spec.ECGenParameterSpec
import java.time.ZonedDateTime
import java.util.Date
import javax.net.ssl.SSLEngine
import javax.net.ssl.X509ExtendedKeyManager
import javax.net.ssl.X509KeyManager
import javax.security.auth.x500.X500Principal

/**
 * The phone's TLS identity in the Android Keystore (docs/ANDROID.md §13): an ECDSA P-256 key that
 * never leaves the secure hardware and a self-signed certificate the Keystore creates for it.
 *
 * `DIGEST_NONE` is required: Conscrypt signs the TLS handshake of a non-exportable key with
 * `NONEwithECDSA`. The key is not backed up (see data_extraction_rules.xml); a new phone pairs again.
 */
internal class KeystoreTlsIdentity private constructor(
    private val privateKey: PrivateKey,
    override val certificate: X509Certificate,
) : TlsIdentity {
    override val keyManager: X509KeyManager =
        object : X509ExtendedKeyManager() {
            override fun chooseClientAlias(
                keyType: Array<out String>?,
                issuers: Array<out Principal>?,
                socket: Socket?,
            ): String? = ALIAS.takeIf { keyType == null || EC in keyType }

            override fun chooseEngineClientAlias(
                keyType: Array<out String>?,
                issuers: Array<out Principal>?,
                engine: SSLEngine?,
            ): String? = chooseClientAlias(keyType, issuers, null)

            override fun getClientAliases(
                keyType: String?,
                issuers: Array<out Principal>?,
            ): Array<String> = arrayOf(ALIAS)

            override fun getCertificateChain(alias: String?): Array<X509Certificate> = arrayOf(certificate)

            override fun getPrivateKey(alias: String?): PrivateKey = privateKey

            // The phone is never a TLS server.
            override fun getServerAliases(
                keyType: String?,
                issuers: Array<out Principal>?,
            ): Array<String> = emptyArray()

            override fun chooseServerAlias(
                keyType: String?,
                issuers: Array<out Principal>?,
                socket: Socket?,
            ): String? = null
        }

    companion object {
        private const val KEYSTORE = "AndroidKeyStore"
        private const val ALIAS = "fenpo.identity.v1"
        private const val EC = "EC"
        private const val VALIDITY_YEARS = 30L

        /** Loads the identity, creating it on first use. Slow the first time; call off the main thread. */
        fun loadOrCreate(deviceId: String): KeystoreTlsIdentity {
            val keyStore = KeyStore.getInstance(KEYSTORE).apply { load(null) }
            val entry = keyStore.getEntry(ALIAS, null) as? KeyStore.PrivateKeyEntry ?: create(deviceId, keyStore)
            return KeystoreTlsIdentity(entry.privateKey, entry.certificate as X509Certificate)
        }

        private fun create(
            deviceId: String,
            keyStore: KeyStore,
        ): KeyStore.PrivateKeyEntry {
            val now = ZonedDateTime.now()
            val spec =
                KeyGenParameterSpec
                    .Builder(ALIAS, KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY)
                    .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                    .setDigests(KeyProperties.DIGEST_NONE, KeyProperties.DIGEST_SHA256)
                    .setCertificateSubject(X500Principal("CN=Fenpo $deviceId"))
                    .setCertificateSerialNumber(BigInteger(64, SecureRandom()).abs().add(BigInteger.ONE))
                    .setCertificateNotBefore(Date.from(now.minusDays(1).toInstant()))
                    .setCertificateNotAfter(Date.from(now.plusYears(VALIDITY_YEARS).toInstant()))
                    .build()
            KeyPairGenerator
                .getInstance(
                    KeyProperties.KEY_ALGORITHM_EC,
                    KEYSTORE,
                ).apply { initialize(spec) }
                .generateKeyPair()
            return keyStore.getEntry(ALIAS, null) as KeyStore.PrivateKeyEntry
        }
    }
}
