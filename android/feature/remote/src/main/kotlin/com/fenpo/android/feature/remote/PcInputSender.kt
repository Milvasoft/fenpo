package com.fenpo.android.feature.remote

import com.fenpo.protocol.v1.PcInput
import com.fenpo.protocol.v1.PcPointerMove
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * Orders the touchpad's input for the PC (pc_input.proto): pointer moves are summed and sent at
 * most every [frameMs] (60 a second), every other event goes right away, after the moves before it,
 * so a click never lands before the move that led to it.
 *
 * @param deliver Sends one input on the best open path. (Not `send`: that name is this class's own
 *   method, and calling it from the queue would put the input back forever.)
 */
class PcInputSender(
    scope: CoroutineScope,
    private val frameMs: Long = FRAME_MS,
    private val deliver: suspend (PcInput) -> Unit,
) {
    private val queue = Channel<PcInput>(Channel.UNLIMITED)
    private var dx = 0f
    private var dy = 0f

    init {
        scope.launch { for (input in queue) deliver(input) }
        scope.launch {
            while (isActive) {
                delay(frameMs)
                flushMove()
            }
        }
    }

    /** Adds to the pending move; the next frame sends the sum. */
    @Synchronized
    fun move(
        deltaX: Float,
        deltaY: Float,
    ) {
        dx += deltaX
        dy += deltaY
    }

    /** Sends [input] after the moves before it. */
    @Synchronized
    fun send(input: PcInput) {
        flushMove()
        queue.trySend(input)
    }

    @Synchronized
    private fun flushMove() {
        if (dx == 0f && dy == 0f) return
        queue.trySend(PcInput(action = PcInput.Action.Move(PcPointerMove(dx = dx, dy = dy))))
        dx = 0f
        dy = 0f
    }

    private companion object {
        const val FRAME_MS = 16L
    }
}
