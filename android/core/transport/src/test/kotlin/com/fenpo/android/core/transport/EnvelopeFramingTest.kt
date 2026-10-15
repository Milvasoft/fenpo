package com.fenpo.android.core.transport

import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Ping
import com.fenpo.protocol.v1.Pong
import okio.Buffer
import okio.ByteString.Companion.decodeHex
import okio.EOFException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class EnvelopeFramingTest {
    private val ping = Envelope(version = 1, id = 7, body = Envelope.Body.Ping(Ping(nonce = 42)))

    @Test
    fun writesBigEndianLengthPrefix() {
        val buffer = Buffer()

        EnvelopeFraming.write(buffer, ping)

        assertEquals("00000008080110075a02082a".decodeHex(), buffer.readByteString())
    }

    @Test
    fun readsFramesBackInOrder() {
        val pong = Envelope(version = 1, id = 8, reply_to = 7, body = Envelope.Body.Pong(Pong(nonce = 42)))
        val buffer = Buffer()
        EnvelopeFraming.write(buffer, ping)
        EnvelopeFraming.write(buffer, pong)

        assertEquals(ping, EnvelopeFraming.read(buffer))
        assertEquals(pong, EnvelopeFraming.read(buffer))
        assertNull(EnvelopeFraming.read(buffer))
    }

    @Test
    fun cleanCloseBetweenFramesReturnsNull() {
        assertNull(EnvelopeFraming.read(Buffer()))
    }

    @Test
    fun closeInsideFrameFails() {
        val truncated = Buffer().write("00000008080110".decodeHex())

        assertFailsWith<EOFException> { EnvelopeFraming.read(truncated) }
    }

    @Test
    fun rejectsOversizedLengthBeforeReadingBody() {
        val hostile = Buffer().write("ffffffff".decodeHex())

        val error = assertFailsWith<FrameTooLargeException> { EnvelopeFraming.read(hostile) }
        assertEquals(0xFFFF_FFFFL, error.size)
    }
}
