package com.fenpo.android.core.transport.testing

import com.fenpo.android.core.transport.Endpoint
import com.fenpo.android.core.transport.tls.EnvelopeConnection
import com.fenpo.android.core.transport.tls.TlsIdentity
import kotlinx.coroutines.runBlocking
import java.io.Closeable
import java.net.InetAddress
import java.net.SocketException
import java.security.KeyStore
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509KeyManager
import javax.net.ssl.X509TrustManager
import kotlin.concurrent.thread

/**
 * TLS helpers for tests: P-256 test certificates (`pc`, `phone`, `other`, generated with keytool)
 * and an in-process server that behaves like the PC side of the data channel.
 */
public object TestTls {
    private const val PASSWORD = "fenpo-test"

    /** Loads the test identity [name] from `fixtures/tls/<name>.p12`. */
    public fun identity(name: String): TlsIdentity {
        val store = KeyStore.getInstance("PKCS12")
        val stream =
            checkNotNull(TestTls::class.java.getResourceAsStream("/fixtures/tls/$name.p12")) { "No fixture $name" }
        stream.use { store.load(it, PASSWORD.toCharArray()) }
        val factory = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm())
        factory.init(store, PASSWORD.toCharArray())
        val keyManager = factory.keyManagers.filterIsInstance<X509KeyManager>().single()
        val certificate = store.getCertificate(name) as X509Certificate
        return object : TlsIdentity {
            override val keyManager = keyManager
            override val certificate = certificate
        }
    }

    /**
     * Starts a TLS server on 127.0.0.1 with [identity] that requires a client certificate and
     * accepts any (as the PC does while pairing). Each accepted connection runs [handler].
     *
     * @param protocols TLS versions the server allows; pass only "TLSv1.2" to test downgrade refusal.
     */
    public fun startServer(
        identity: TlsIdentity,
        protocols: Array<String> = arrayOf("TLSv1.3"),
        handler: suspend (EnvelopeConnection) -> Unit,
    ): TestTlsServer {
        val context = SSLContext.getInstance("TLS")
        context.init(arrayOf(identity.keyManager), arrayOf(AcceptAnyClient), SecureRandom())
        val server =
            context.serverSocketFactory.createServerSocket(
                0,
                8,
                InetAddress.getLoopbackAddress(),
            ) as SSLServerSocket
        server.enabledProtocols = protocols
        server.needClientAuth = true
        return TestTlsServer(server, handler)
    }

    private object AcceptAnyClient : X509TrustManager {
        override fun checkClientTrusted(
            chain: Array<out X509Certificate>?,
            authType: String?,
        ): Unit = Unit

        override fun checkServerTrusted(
            chain: Array<out X509Certificate>?,
            authType: String?,
        ): Unit = throw UnsupportedOperationException()

        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }
}

/** A running [TestTls.startServer]. Collects errors from the handler so tests can assert on them. */
public class TestTlsServer internal constructor(
    private val server: SSLServerSocket,
    private val handler: suspend (EnvelopeConnection) -> Unit,
) : Closeable {
    /** Where to connect. */
    public val endpoint: Endpoint = Endpoint("127.0.0.1", server.localPort)

    /** Failures on the server side, including handshakes the server rejected. */
    public val errors: MutableList<Throwable> = CopyOnWriteArrayList()

    private val handled = CountDownLatch(1)

    init {
        thread(name = "test-tls-server", isDaemon = true) {
            while (!server.isClosed) {
                val socket =
                    try {
                        server.accept() as SSLSocket
                    } catch (_: SocketException) {
                        break
                    }
                try {
                    socket.startHandshake()
                    EnvelopeConnection(socket).use { runBlocking { handler(it) } }
                } catch (e: Throwable) {
                    errors += e
                    runCatching { socket.close() }
                } finally {
                    handled.countDown()
                }
            }
        }
    }

    /** Waits until at least one connection was handled (or failed). */
    public fun awaitFirstConnection(seconds: Long = 10): Boolean = handled.await(seconds, TimeUnit.SECONDS)

    override fun close() {
        runCatching { server.close() }
    }
}
