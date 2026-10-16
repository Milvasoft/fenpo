package com.fenpo.android.core.transport

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class EndpointTest {
    @Test
    fun parsesIpv4AndBracketedIpv6() {
        assertEquals(Endpoint("192.168.1.20", 52817), Endpoint.parse("192.168.1.20:52817"))
        assertEquals(Endpoint("fe80::1%wlan0", 52817), Endpoint.parse("[fe80::1%wlan0]:52817"))
    }

    @Test
    fun roundTripsThroughToString() {
        for (endpoint in listOf(Endpoint("10.0.0.5", 1), Endpoint("2001:db8::7", 65535))) {
            assertEquals(endpoint, Endpoint.parse(endpoint.toString()))
        }
    }

    @Test
    fun rejectsMissingOrBadPortsAndBareIpv6() {
        assertNull(Endpoint.parse("192.168.1.20"))
        assertNull(Endpoint.parse("192.168.1.20:0"))
        assertNull(Endpoint.parse("192.168.1.20:70000"))
        assertNull(Endpoint.parse("192.168.1.20:port"))
        assertNull(Endpoint.parse("fe80::1:52817"))
        assertNull(Endpoint.parse("[fe80::1]52817"))
        assertNull(Endpoint.parse(":52817"))
    }
}
