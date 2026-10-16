package com.fenpo.android.core.media

import org.junit.Test
import kotlin.test.assertEquals

class BitrateControllerTest {
    @Test
    fun dropsByAQuarterDownToTheFloorAndRecoversSlowly() {
        val bitrate = BitrateController(targetKbps = 6000, floorKbps = 1500)

        assertEquals(4500, bitrate.onDrop(0))
        assertEquals(3375, bitrate.onDrop(100))
        repeat(10) { bitrate.onDrop(200) }
        assertEquals(1500, bitrate.currentKbps)

        assertEquals(1500, bitrate.onTick(5_000))
        assertEquals(1650, bitrate.onTick(10_200))
        assertEquals(1650, bitrate.onTick(15_000))
        assertEquals(1815, bitrate.onTick(20_200))
    }

    @Test
    fun neverGoesAboveTheTarget() {
        val bitrate = BitrateController(targetKbps = 6000, floorKbps = 1500)
        bitrate.onDrop(0)
        var now = 10_000L
        repeat(20) {
            bitrate.onTick(now)
            now += 10_000
        }
        assertEquals(6000, bitrate.currentKbps)
    }
}
