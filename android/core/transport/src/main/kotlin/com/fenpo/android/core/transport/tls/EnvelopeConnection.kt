package com.fenpo.android.core.transport.tls

import com.fenpo.android.core.protocol.ProtocolVersion
import com.fenpo.android.core.transport.EnvelopeFraming
import com.fenpo.protocol.v1.Envelope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import okio.ByteString
import okio.buffer
import okio.sink
import okio.source
import java.io.Closeable
import java.security.cert.X509Certificate
import java.util.concurrent.atomic.AtomicLong
import javax.net.ssl.SSLSocket
import kotlin.time.Duration

/**
 * A TLS connection that carries framed [Envelope]s. Sending is safe from several coroutines;
 * receiving is meant for one reader at a time.
 */
public class EnvelopeConnection(
    private val socket: SSLSocket,
) : Closeable {
    private val source = socket.inputStream.source().buffer()
    private val sink = socket.outputStream.sink().buffer()
    private val writeLock = Mutex()
    private val nextId = AtomicLong(1)

    /** SHA-256 of the certificate the other side presented. */
    public val peerCertificateSha256: ByteString =
        (socket.session.peerCertificates.first() as X509Certificate).sha256()

    /** Negotiated TLS version, e.g. "TLSv1.3". */
    public val protocol: String = socket.session.protocol

    /**
     * Sends [body] in a new envelope and returns its id.
     *
     * @param replyTo Id of the request this message answers, 0 if none.
     */
    public suspend fun send(
        body: Envelope.Body,
        replyTo: Long = 0,
    ): Long {
        val id = nextId.getAndIncrement()
        val envelope = Envelope(version = ProtocolVersion.V1, id = id, reply_to = replyTo, body = body)
        writeLock.withLock {
            withContext(Dispatchers.IO) { EnvelopeFraming.write(sink, envelope) }
        }
        return id
    }

    /**
     * Waits up to [timeout] for the next envelope.
     *
     * @return the envelope, or `null` if the other side closed the connection.
     * @throws java.net.SocketTimeoutException if nothing arrived in time.
     */
    public suspend fun receive(timeout: Duration): Envelope? =
        withContext(Dispatchers.IO) {
            socket.soTimeout = timeout.inWholeMilliseconds.toInt().coerceAtLeast(1)
            EnvelopeFraming.read(source)
        }

    override fun close() {
        runCatching { socket.close() }
    }
}
