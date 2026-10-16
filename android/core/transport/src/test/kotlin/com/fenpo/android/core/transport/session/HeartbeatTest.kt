package com.fenpo.android.core.transport.session

import com.fenpo.protocol.v1.Envelope
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue
import kotlin.time.Duration.Companion.seconds

class HeartbeatTest {
    @Test
    fun keepsRunningWhilePongsArrive() =
        runTest {
            lateinit var heartbeat: Heartbeat
            var pings = 0
            heartbeat =
                Heartbeat(interval = 30.seconds, timeout = 10.seconds) { body ->
                    pings++
                    heartbeat.onPong((body as Envelope.Body.Ping).value.nonce)
                }
            val job = launch { heartbeat.run() }

            advanceTimeBy(5 * 30_000L + 1)

            assertEquals(5, pings)
            assertTrue(job.isActive)
            job.cancel()
        }

    @Test
    fun returnsWhenAPingGoesUnanswered() =
        runTest {
            var pings = 0
            val heartbeat = Heartbeat(interval = 30.seconds, timeout = 10.seconds) { pings++ }
            val job = launch { heartbeat.run() }

            advanceTimeBy(30_000L + 9_000L)
            assertTrue(job.isActive, "still within the timeout")

            advanceTimeBy(2_000L)
            assertFalse(job.isActive, "gave up after the timeout")
            assertEquals(1, pings)
        }

    @Test
    fun ignoresPongForAnotherNonce() =
        runTest {
            lateinit var heartbeat: Heartbeat
            heartbeat = Heartbeat(interval = 30.seconds, timeout = 10.seconds) { heartbeat.onPong(-1) }
            val job = launch { heartbeat.run() }

            advanceTimeBy(41_000L)

            assertFalse(job.isActive)
        }
}
