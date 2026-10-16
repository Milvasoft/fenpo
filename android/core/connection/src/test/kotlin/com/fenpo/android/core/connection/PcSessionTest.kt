package com.fenpo.android.core.connection

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.DeviceState
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.PcStatus
import com.fenpo.protocol.v1.Ping
import com.fenpo.protocol.v1.Pong
import kotlinx.coroutines.test.runTest
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
class PcSessionTest {
    private val status = ConnectionStatus()
    private val sent = mutableListOf<Pair<Envelope.Body, Long>>()
    private val addresses = mutableListOf<List<String>>()
    private val session =
        PcSession(
            LinkPath.WIFI,
            status,
            transport = { body, replyTo -> sent += body to replyTo },
            onAddresses = { addresses += it },
        )

    @Test
    fun helloFromThePcOpensTheSession() =
        runTest {
            val hello =
                Hello(
                    device_id = "pc",
                    platform = DevicePlatform.DEVICE_PLATFORM_WINDOWS,
                    capabilities = listOf(Capability.CAPABILITY_DEVICE_STATE),
                )

            session.onEnvelope(Envelope(id = 1, body = Envelope.Body.Hello(hello)))

            assertEquals(hello, session.awaitHello())
            assertEquals(hello, status.snapshot.value.remote)
        }

    @Test
    fun answersPingWithTheSameNonce() =
        runTest {
            session.onEnvelope(Envelope(id = 7, body = Envelope.Body.Ping(Ping(nonce = 42))))

            assertEquals(listOf<Pair<Envelope.Body, Long>>(Envelope.Body.Pong(Pong(nonce = 42)) to 7L), sent)
        }

    @Test
    fun keepsPcStatusAndAddresses() =
        runTest {
            val pcStatus = PcStatus(notifications_subscribed = true)
            session.onEnvelope(Envelope(id = 2, body = Envelope.Body.PcStatus(pcStatus)))
            session.onEnvelope(
                Envelope(id = 3, body = Envelope.Body.DeviceState(DeviceState(addresses = listOf("10.0.0.5:52817")))),
            )
            session.onEnvelope(Envelope(id = 4, body = Envelope.Body.DeviceState(DeviceState(battery_percent = 80))))

            assertEquals(pcStatus, status.snapshot.value.pcStatus)
            assertEquals(listOf(listOf("10.0.0.5:52817")), addresses)
        }
}
