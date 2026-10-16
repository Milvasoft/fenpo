package com.fenpo.android.core.connection

import com.fenpo.android.core.transport.Endpoint
import org.junit.Test
import kotlin.test.assertEquals

class AddressOrderTest {
    @Test
    fun workingAddressGoesFirstWithoutDuplicates() {
        assertEquals(
            listOf("10.0.0.9:1", "10.0.0.5:1", "[fe80::1]:1"),
            AddressOrder.withFirst("10.0.0.9:1", listOf("10.0.0.5:1", "10.0.0.9:1", "[fe80::1]:1")),
        )
    }

    @Test
    fun triesThePhonesSubnetFirstAndLinkLocalLast() {
        val pc =
            listOf(
                "169.254.83.107:53113",
                "172.27.144.1:53113",
                "[fe80::1]:53113",
                "192.168.1.14:53113",
                "[2a02::5]:53113",
            ).mapNotNull(Endpoint::parse)

        val ordered = AddressOrder.forNetwork(pc, listOf("192.168.1.6", "fe80::aa"))

        assertEquals(
            listOf(
                "192.168.1.14:53113",
                "172.27.144.1:53113",
                "[2a02::5]:53113",
                "169.254.83.107:53113",
                "[fe80::1]:53113",
            ),
            ordered.map { it.toString() },
        )
    }

    @Test
    fun keepsAtMostEight() {
        val many = (1..20).map { "10.0.0.$it:1" }
        assertEquals(8, AddressOrder.withFirst("10.0.0.99:1", many).size)
    }
}
