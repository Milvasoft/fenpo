package com.fenpo.android.core.transport

import com.fenpo.protocol.v1.TransferStreamHeader
import okio.Buffer
import okio.BufferedSink
import okio.BufferedSource
import okio.EOFException
import okio.IOException
import okio.Sink

/**
 * The data connection of a file transfer (transfer.proto, protocol/README "File transfer"): the
 * phone opens it and writes `[uint32 big-endian length][TransferStreamHeader]`; then the sender
 * writes the raw bytes of each file in offer order and closes after the last byte.
 */
public object TransferStream {
    private const val MAX_HEADER_BYTES = 4096
    private const val CHUNK_BYTES = 64L * 1024

    /** Writes the framed header and flushes it. */
    public fun writeHeader(
        sink: BufferedSink,
        header: TransferStreamHeader,
    ) {
        val bytes = TransferStreamHeader.ADAPTER.encode(header)
        sink.writeInt(bytes.size)
        sink.write(bytes)
        sink.flush()
    }

    /** Reads the framed header; for tests and a future phone-side listener. */
    public fun readHeader(source: BufferedSource): TransferStreamHeader {
        val length = source.readInt().toLong() and 0xFFFF_FFFFL
        if (length > MAX_HEADER_BYTES) throw IOException("Stream header of $length bytes")
        return TransferStreamHeader.ADAPTER.decode(source.readByteArray(length))
    }

    /**
     * Copies exactly [size] bytes from [source] to [sink], calling [onBytes] with the running
     * count after every chunk.
     *
     * @throws EOFException if [source] ends before [size] bytes.
     */
    public fun copyExactly(
        source: BufferedSource,
        sink: Sink,
        size: Long,
        onBytes: (Long) -> Unit = {},
    ) {
        val buffer = Buffer()
        var done = 0L
        while (done < size) {
            val read = source.read(buffer, minOf(CHUNK_BYTES, size - done))
            if (read < 0) throw EOFException("Stream ended after $done of $size bytes")
            sink.write(buffer, read)
            done += read
            onBytes(done)
        }
        sink.flush()
    }
}
