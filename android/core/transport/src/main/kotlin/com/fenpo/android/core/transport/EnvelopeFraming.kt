package com.fenpo.android.core.transport

import com.fenpo.protocol.v1.Envelope
import okio.BufferedSink
import okio.BufferedSource
import okio.EOFException
import okio.IOException

/**
 * Framing of the TLS control connection: every message is `[uint32 big-endian length][Envelope
 * bytes]` (docs/ARCHITECTURE.md, "Çerçeveleme").
 */
public object EnvelopeFraming {
    /** Length prefix size in bytes. */
    public const val HEADER_BYTES: Int = 4

    /**
     * Largest Envelope accepted on the control connection, the same limit as the PC (S5). Clipboard
     * images above 512 KB and all files travel on separate data connections.
     */
    public const val MAX_ENVELOPE_BYTES: Int = 1024 * 1024

    /**
     * Writes one framed [envelope] to [sink] and flushes it.
     *
     * @throws FrameTooLargeException if the encoded envelope is larger than [MAX_ENVELOPE_BYTES].
     */
    public fun write(
        sink: BufferedSink,
        envelope: Envelope,
    ) {
        val bytes = Envelope.ADAPTER.encode(envelope)
        if (bytes.size > MAX_ENVELOPE_BYTES) throw FrameTooLargeException(bytes.size.toLong())
        sink.writeInt(bytes.size)
        sink.write(bytes)
        sink.flush()
    }

    /**
     * Reads the next framed envelope from [source].
     *
     * @return the envelope, or `null` when the peer closed the connection between frames.
     * @throws EOFException if the connection closed in the middle of a frame.
     * @throws FrameTooLargeException if the announced length exceeds [MAX_ENVELOPE_BYTES].
     */
    public fun read(source: BufferedSource): Envelope? {
        if (source.exhausted()) return null
        val length = source.readInt().toLong() and 0xFFFF_FFFFL
        if (length > MAX_ENVELOPE_BYTES) throw FrameTooLargeException(length)
        val bytes = source.readByteArray(length)
        return Envelope.ADAPTER.decode(bytes)
    }
}

/** A frame announced or produced a size above [EnvelopeFraming.MAX_ENVELOPE_BYTES]. */
public class FrameTooLargeException(
    public val size: Long,
) : IOException("Envelope of $size bytes exceeds ${EnvelopeFraming.MAX_ENVELOPE_BYTES} bytes")
