package com.fenpo.android.feature.mirror

import org.junit.Test
import kotlin.test.assertEquals

class ScreenSizeTest {
    @Test
    fun capsTheLongSideAndKeepsSidesEven() {
        // Galaxy S10+: 1440 x 3040 portrait.
        assertEquals(908 to 1920, ScreenSize.fit(1440, 3040, 1920))
        assertEquals(1920 to 908, ScreenSize.fit(3040, 1440, 1920))
        assertEquals(1080 to 2340, ScreenSize.fit(1081, 2340, 2400))
    }
}
