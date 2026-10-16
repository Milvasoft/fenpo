package com.fenpo.android.core.transport.session

import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Ping
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.delay
import kotlinx.coroutines.withTimeoutOrNull
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong
import kotlin.random.Random
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/**
 * Application-level liveness check with `Ping`/`Pong` (hello.proto). A BLE link can stay up while
 * nothing on the PC listens any more (device test A2: the GATT link survived the service being
 * removed, Android reused its cached GATT table), so only an answered `Ping` proves the other side.
 *
 * @param send Sends one message body on the channel being checked.
 */
public class Heartbeat(
    private val interval: Duration = 30.seconds,
    private val timeout: Duration = 10.seconds,
    private val send: suspend (Envelope.Body) -> Unit,
) {
    private val waiting = ConcurrentHashMap<Long, CompletableDeferred<Unit>>()
    private val nonces = AtomicLong(Random.nextLong())

    /** Pings every [interval]; returns as soon as one `Ping` goes unanswered for [timeout]. */
    public suspend fun run() {
        while (true) {
            delay(interval)
            val nonce = nonces.incrementAndGet()
            val answered = CompletableDeferred<Unit>().also { waiting[nonce] = it }
            try {
                send(Envelope.Body.Ping(Ping(nonce = nonce)))
                if (withTimeoutOrNull(timeout) { answered.await() } == null) return
            } finally {
                waiting.remove(nonce)
            }
        }
    }

    /** Call for every `Pong` received on the channel. */
    public fun onPong(nonce: Long) {
        waiting[nonce]?.complete(Unit)
    }
}
