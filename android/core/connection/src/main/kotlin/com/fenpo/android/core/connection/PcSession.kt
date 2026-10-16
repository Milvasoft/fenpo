package com.fenpo.android.core.connection

import android.util.Log
import com.fenpo.android.core.transport.session.Heartbeat
import com.fenpo.android.core.transport.tls.DataConnection
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.Hello
import com.fenpo.protocol.v1.Pong
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi

/** The two ways the phone reaches the PC (docs/ARCHITECTURE.md, ③ and ④). */
enum class LinkPath { BLUETOOTH, WIFI }

/**
 * What every channel to the PC does with the PC's messages, whichever path it runs on: `Hello`,
 * `Ping`/`Pong`, `PcStatus` and the PC's addresses in `DeviceState`. Everything else goes to
 * [onOther] for the features.
 *
 * @param transport Sends one body on this channel, answering the envelope id given (0 if none).
 * @param onAddresses Called with the PC's current data channel addresses.
 * @param openData Opens a file transfer data connection to the PC on this path (Wi-Fi only);
 *   the argument is `TransferAccept.data_port`, 0 for the control port.
 */
internal class PcSession(
    val path: LinkPath,
    private val status: ConnectionStatus,
    private val transport: suspend (Envelope.Body, Long) -> Unit,
    private val onAddresses: suspend (List<String>) -> Unit,
    private val onOther: (Envelope) -> Unit = {},
    val openData: (suspend (Int) -> DataConnection)? = null,
) {
    private val hello = CompletableDeferred<Hello>()

    /** Started by the link once the PC's `Hello` arrived; only answered Pings keep the channel. */
    val heartbeat = Heartbeat { transport(it, 0) }

    /** Waits for the PC's `Hello` on this channel. */
    suspend fun awaitHello(): Hello = hello.await()

    /** The PC's `Hello` on this channel, if it arrived. */
    @OptIn(ExperimentalCoroutinesApi::class)
    fun helloOrNull(): Hello? = if (hello.isCompleted) hello.getCompleted() else null

    /** Sends [body] on this channel, answering [replyTo] (0 if none). */
    suspend fun send(
        body: Envelope.Body,
        replyTo: Long = 0,
    ) = transport(body, replyTo)

    suspend fun onEnvelope(envelope: Envelope) {
        when (val body = envelope.body) {
            is Envelope.Body.Hello -> {
                Log.i(TAG, "Hello from the PC on $path: ${body.value.os_version}")
                hello.complete(body.value)
                status.update { it.copy(remote = body.value) }
            }

            is Envelope.Body.Ping -> {
                transport(Envelope.Body.Pong(Pong(nonce = body.value.nonce)), envelope.id)
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
                onOther(envelope)
            }
        }
    }

    private companion object {
        const val TAG = "FenpoConnection"
    }
}
