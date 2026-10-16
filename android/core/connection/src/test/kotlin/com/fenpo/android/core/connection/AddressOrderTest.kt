package com.fenpo.android.core.connection

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
    fun keepsAtMostEight() {
        val many = (1..20).map { "10.0.0.$it:1" }
        assertEquals(8, AddressOrder.withFirst("10.0.0.99:1", many).size)
    }
}
