package com.fenpo.android.core.connection

import android.util.Log
import com.fenpo.android.core.transport.session.Heartbeat
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.Pong
import kotlinx.coroutines.CompletableDeferred

/** The two ways the phone reaches the PC (docs/ARCHITECTURE.md, ③ and ④). */
enum class LinkPath { BLUETOOTH, WIFI }

/**
 * What every channel to the PC does with the PC's messages, whichever path it runs on: `Hello`,
 * `Ping`/`Pong`, `PcStatus` and the PC's addresses in `DeviceState`.
 *
 * @param send Sends one body on this channel, answering the envelope id given (0 if none).
 * @param onAddresses Called with the PC's current data channel addresses.
 */
internal class PcSession(
    val path: LinkPath,
    private val status: ConnectionStatus,
    private val send: suspend (Envelope.Body, Long) -> Unit,
    private val onAddresses: suspend (List<String>) -> Unit,
) {
    private val hello = CompletableDeferred<Hello>()

    /** Started by the link once the PC's `Hello` arrived; only answered Pings keep the channel. */
    val heartbeat = Heartbeat { send(it, 0) }

    /** Waits for the PC's `Hello` on this channel. */
    suspend fun awaitHello(): Hello = hello.await()

    /** Sends [body] on this channel. */
    suspend fun send(body: Envelope.Body) = send(body, 0)

    suspend fun onEnvelope(envelope: Envelope) {
        when (val body = envelope.body) {
            is Envelope.Body.Hello -> {
                Log.i(TAG, "Hello from the PC on $path: ${body.value.os_version}")
                hello.complete(body.value)
                status.update { it.copy(remote = body.value) }
            }

            is Envelope.Body.Ping -> {
                send(Envelope.Body.Pong(Pong(nonce = body.value.nonce)), envelope.id)
            }

            is Envelope.Body.Pong -> {
                heartbeat.onPong(body.value.nonce)
            }

            is Envelope.Body.PcStatus -> {
                status.update { it.copy(pcStatus = body.value) }
            }

            is Envelope.Body.DeviceState -> {
                if (body.value.addresses.isNotEmpty()) onAddresses(body.value.addresses)
            }

            else -> {
                Log.d(TAG, "Unhandled ${body?.javaClass?.simpleName} on $path")
            }
        }
    }

    private companion object {
        const val TAG = "FenpoConnection"
    }
}
