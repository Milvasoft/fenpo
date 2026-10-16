package com.fenpo.android.feature.camera

import org.junit.Test
import kotlin.test.assertEquals

class RotationTest {
    @Test
    fun turnsTheBackCameraUprightForEveryHold() {
        // Typical back sensor mounted at 90°.
        assertEquals(90, GlFrameRotator.rotationFor(90, 0, front = false))
        assertEquals(0, GlFrameRotator.rotationFor(90, 270, front = false))
        assertEquals(180, GlFrameRotator.rotationFor(90, 90, front = false))
        assertEquals(0, GlFrameRotator.rotationFor(90, 265, front = false))
    }

    @Test
    fun turnsTheFrontCameraTheOtherWay() {
        // Typical front sensor mounted at 270°.
        assertEquals(270, GlFrameRotator.rotationFor(270, 0, front = true))
        assertEquals(0, GlFrameRotator.rotationFor(270, 270, front = true))
        assertEquals(180, GlFrameRotator.rotationFor(270, 90, front = true))
    }

    @Test
    fun anglesWrapAround() {
        assertEquals(44, GlFrameRotator.angleBetween(316, 0))
        assertEquals(45, GlFrameRotator.angleBetween(45, 90))
        assertEquals(180, GlFrameRotator.angleBetween(0, 180))
        assertEquals(20, GlFrameRotator.angleBetween(350, 10))
    }
}
