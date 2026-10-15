package com.fenpo.android.core.security

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.transport.testing.TestTls
import com.fenpo.android.core.transport.tls.TlsConnector
import com.fenpo.android.core.transport.tls.sha256
import kotlinx.coroutines.test.runTest
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.time.Duration.Companion.seconds

/**
 * Runs on a phone or emulator (docs/ANDROID.md §21, A3): the Keystore key must work as a TLS 1.3
 * client certificate through Conscrypt, which is exactly what Robolectric cannot check.
 */
@RunWith(AndroidJUnit4::class)
class KeystoreTlsTest {
    @Test
    fun keystoreIdentityAuthenticatesOverTls13() =
        runTest {
            val identity = KeystoreIdentityProvider().identity("instrumented-test")
            val pc = TestTls.identity("pc")
            var seenByServer = ""
            TestTls.startServer(pc) { seenByServer = it.peerCertificateSha256.hex() }.use { server ->
                TlsConnector(identity).connect(server.endpoint, pc.certificate.sha256()).use {
                    assertEquals("TLSv1.3", it.protocol)
                }
                server.awaitFirstConnection(10)
            }
            assertEquals(identity.certificate.sha256().hex(), seenByServer)
        }

    @Test
    fun identitySurvivesReload() =
        runTest(timeout = 30.seconds) {
            val first = KeystoreTlsIdentity.loadOrCreate("instrumented-test").certificate
            val second = KeystoreTlsIdentity.loadOrCreate("instrumented-test").certificate
            assertEquals(first.sha256(), second.sha256())
        }

    @Test
    fun secretBoxRoundTrips() {
        val box = KeystoreSecretBox()
        val secret = ByteArray(32) { it.toByte() }
        assertContentEquals(secret, box.open(box.seal(secret)))
    }
}
