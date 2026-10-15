package com.fenpo.android.core.transport.session

import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.Hello
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class HelloNegotiationTest {
    private val phone =
        LocalDevice(
            id = "phone",
            name = "Pixel 9",
            appVersion = "0.1.0",
            capabilities = setOf(Capability.CAPABILITY_CLIPBOARD, Capability.CAPABILITY_RING),
            protocolVersions = listOf(1),
        ).hello()

    @Test
    fun keepsOnlySharedCapabilitiesAndHighestCommonVersion() {
        val pc =
            Hello(
                platform = DevicePlatform.DEVICE_PLATFORM_WINDOWS,
                protocol_versions = listOf(1, 2),
                capabilities = listOf(Capability.CAPABILITY_CLIPBOARD, Capability.CAPABILITY_LOCK_PC),
            )

        val session = HelloNegotiation.negotiate(phone, pc)

        assertEquals(1, session?.protocolVersion)
        assertEquals(setOf(Capability.CAPABILITY_CLIPBOARD), session?.capabilities)
    }

    @Test
    fun failsWithoutCommonVersion() {
        assertNull(HelloNegotiation.negotiate(phone, Hello(protocol_versions = listOf(2))))
    }

    @Test
    fun phoneHelloIdentifiesAndroid() {
        assertEquals(DevicePlatform.DEVICE_PLATFORM_ANDROID, phone.platform)
        assertEquals(listOf(Capability.CAPABILITY_CLIPBOARD, Capability.CAPABILITY_RING), phone.capabilities)
    }
}
