package com.fenpo.android.feature.remote

import com.fenpo.protocol.v1.PcClick
import com.fenpo.protocol.v1.PcInput
import com.fenpo.protocol.v1.PcPointerMove
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Test
import kotlin.test.assertEquals

class PcInputSenderTest {
    @Test
    fun sumsMovesPerFrameAndKeepsClicksAfterThem() =
        runTest {
            val sent = mutableListOf<PcInput>()
            // backgroundScope ends with the test, so the frame loop does not keep it running.
            val sender = PcInputSender(backgroundScope, frameMs = 16) { sent += it }

            sender.move(1f, 2f)
            sender.move(3f, 4f)
            advanceTimeBy(17)
            runCurrent()
            sender.move(5f, 0f)
            sender.send(PcInput(action = PcInput.Action.Click(PcClick())))
            runCurrent()

            assertEquals(
                listOf(
                    PcInput(action = PcInput.Action.Move(PcPointerMove(dx = 4f, dy = 6f))),
                    PcInput(action = PcInput.Action.Move(PcPointerMove(dx = 5f, dy = 0f))),
                    PcInput(action = PcInput.Action.Click(PcClick())),
                ),
                sent,
            )
        }
}
