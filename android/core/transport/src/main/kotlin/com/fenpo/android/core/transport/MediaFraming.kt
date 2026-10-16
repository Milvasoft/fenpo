package com.fenpo.android.core.transport

import com.fenpo.android.core.protocol.ProtocolVersion
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.MediaStreamOpen
import com.fenpo.protocol.v1.StreamPacket
import okio.BufferedSink
import okio.BufferedSource
import okio.IOException

/**
 * Framing of a media connection (video.proto, protocol/README "Media connections"): the phone
 * writes `[uint32 length][Envelope{media_stream_open}]`, then `[uint32 length][StreamPacket]` for
 * every encoded access unit.
 */
public object MediaFraming {
    /** A key frame of a high bitrate stream fits easily; anything larger is a bug. */
    public const val MAX_PACKET_BYTES: Int = 4 * 1024 * 1024

    public fun writeOpen(
        sink: BufferedSink,
        open: MediaStreamOpen,
    ) {
        EnvelopeFraming.write(
            sink,
            Envelope(version = ProtocolVersion.V1, id = 1, body = Envelope.Body.MediaStreamOpen(open)),
        )
    }

    /** Writes one packet; the caller flushes, so several small audio packets can share a write. */
    public fun writePacket(
        sink: BufferedSink,
        packet: StreamPacket,
    ) {
        val bytes = StreamPacket.ADAPTER.encode(packet)
        if (bytes.size > MAX_PACKET_BYTES) throw IOException("Stream packet of ${bytes.size} bytes")
        sink.writeInt(bytes.size)
        sink.write(bytes)
    }

    /** Reads the opening frame; for tests and a PC written in Kotlin. */
    public fun readOpen(source: BufferedSource): MediaStreamOpen? =
        (EnvelopeFraming.read(source)?.body as? Envelope.Body.MediaStreamOpen)?.value

    /** Reads the next packet, or `null` at the end of the stream. */
    public fun readPacket(source: BufferedSource): StreamPacket? {
        if (source.exhausted()) return null
        val length = source.readInt().toLong() and 0xFFFF_FFFFL
        if (length > MAX_PACKET_BYTES) throw IOException("Stream packet of $length bytes")
        return StreamPacket.ADAPTER.decode(source.readByteArray(length))
    }
}
