package com.fenpo.android.feature.transfers

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.fenpo.android.core.testing.captureScreenVariants
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.GraphicsMode

@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class TransfersScreenshotTest {
    @Test
    fun transfersEmpty() = captureScreenVariants("transfers_empty") { TransfersScreen() }
}
