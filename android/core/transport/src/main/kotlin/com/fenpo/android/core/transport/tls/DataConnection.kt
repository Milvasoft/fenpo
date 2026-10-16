package com.fenpo.android.core.transport.tls

import okio.BufferedSink
import okio.BufferedSource
import okio.buffer
import okio.sink
import okio.source
import java.io.Closeable
import javax.net.ssl.SSLSocket
import kotlin.time.Duration

/**
 * A TLS connection that carries one file transfer: `[uint32 length][TransferStreamHeader]` from the
 * phone, then the raw bytes of every file in offer order (transfer.proto). [TransferStream] reads
 * and writes those parts.
 */
public class DataConnection(
    private val socket: SSLSocket,
) : Closeable {
    public val source: BufferedSource = socket.inputStream.source().buffer()
    public val sink: BufferedSink = socket.outputStream.sink().buffer()

    /** How long one read may wait before failing with a timeout. */
    public fun setReadTimeout(timeout: Duration) {
        socket.soTimeout = timeout.inWholeMilliseconds.toInt().coerceAtLeast(1)
    }

    override fun close() {
        runCatching { socket.close() }
    }
}
