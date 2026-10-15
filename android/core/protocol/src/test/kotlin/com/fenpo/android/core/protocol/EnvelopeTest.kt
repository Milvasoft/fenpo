package com.fenpo.android.core.protocol

import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.Ping
import okio.ByteString.Companion.decodeHex
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

class EnvelopeTest {
    @Test
    fun helloRoundTrips() {
        val hello =
            Hello(
                device_id = "6f1c2b9e-0d7a-4a55-9d0e-2f9a6c1e8b11",
                device_name = "Pixel 9",
                platform = DevicePlatform.DEVICE_PLATFORM_ANDROID,
                app_version = "0.1.0",
                protocol_versions = ProtocolVersion.supported,
                capabilities = listOf(Capability.CAPABILITY_CLIPBOARD, Capability.CAPABILITY_FILE_TRANSFER),
            )
        val envelope = Envelope(version = ProtocolVersion.V1, id = 1, body = Envelope.Body.Hello(hello))

        val decoded = Envelope.ADAPTER.decode(Envelope.ADAPTER.encode(envelope))

        val body = assertIs<Envelope.Body.Hello>(decoded.body)
        assertEquals(hello, body.value)
        assertEquals(1, decoded.version)
    }

    @Test
    fun pingMatchesTheWireFormat() {
        // version = 1, id = 7, ping { nonce = 42 }: field 11, length-delimited.
        val envelope = Envelope(version = 1, id = 7, body = Envelope.Body.Ping(Ping(nonce = 42)))

        assertEquals("080110075a02082a".decodeHex(), Envelope.ADAPTER.encodeByteString(envelope))
    }
}
