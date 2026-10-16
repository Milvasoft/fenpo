package com.fenpo.android.core.connection

import android.util.Log
import com.fenpo.android.core.ble.TooLargeForBleException
import com.fenpo.android.core.transport.tls.DataConnection
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.updateAndGet
import kotlinx.coroutines.withTimeoutOrNull
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/**
 * How features talk to the PC without knowing which path is open: sends go on Wi-Fi when it is up,
 * else on Bluetooth; messages the session layer does not handle itself arrive on [incoming].
 */
@Singleton
class PcMessenger
    @Inject
    constructor() {
        /** Sessions that finished `Hello`, by path. */
        internal val sessions = MutableStateFlow<Map<LinkPath, PcSession>>(emptyMap())

        private val _capabilities = MutableStateFlow<Set<Capability>>(emptySet())

        /** What the PC offers in its `Hello` on the open paths; empty while not connected. */
        val capabilities: StateFlow<Set<Capability>> = _capabilities.asStateFlow()

        private val _incoming = MutableSharedFlow<Envelope>(extraBufferCapacity = INCOMING_BUFFER)

        /** PC messages for features: clipboard, links, ring, transfers. */
        val incoming: SharedFlow<Envelope> = _incoming.asSharedFlow()

        /** `true` while at least one path has finished `Hello`. */
        val connected: Boolean get() = sessions.value.isNotEmpty()

        /** `true` while the Wi-Fi path is open; files need it. */
        val wifiOpen: Boolean get() = sessions.value[LinkPath.WIFI] != null

        /**
         * Sends [body] on the best open path, if the PC offers [capability].
         *
         * @return `false` when no path is open, the PC does not offer [capability], or the message
         *   needs Wi-Fi (too large for Bluetooth) and only Bluetooth is up.
         */
        suspend fun send(
            body: Envelope.Body,
            capability: Capability? = null,
            replyTo: Long = 0,
        ): Boolean {
            if (capability != null && capability !in _capabilities.value) return false
            val open = sessions.value
            for (path in PREFERENCE) {
                val session = open[path] ?: continue
                try {
                    session.send(body, replyTo)
                    return true
                } catch (e: CancellationException) {
                    throw e
                } catch (e: TooLargeForBleException) {
                    Log.d(TAG, "Too large for Bluetooth: ${body.javaClass.simpleName}")
                } catch (e: Exception) {
                    Log.d(TAG, "Not sent on $path: ${e.message}")
                }
            }
            return false
        }

        private val waiting = HashMap<Long, CompletableDeferred<Envelope>>()
        private val earlyAnswers = LinkedHashMap<Long, Envelope>()

        /**
         * Sends [body] on Wi-Fi and waits for the PC's answer (the envelope whose `reply_to` is this
         * one's id): folder listings, previews and the like.
         *
         * @return the answer, which may be an `Error`; `null` when Wi-Fi is not open, the PC does not
         *   offer [capability], or nothing came back within [timeout].
         */
        suspend fun request(
            body: Envelope.Body,
            capability: Capability,
            timeout: Duration = REQUEST_TIMEOUT,
        ): Envelope? {
            if (capability !in _capabilities.value) return null
            val session = sessions.value[LinkPath.WIFI] ?: return null
            val id =
                try {
                    session.send(body)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    Log.d(TAG, "Request not sent: ${e.message}")
                    return null
                }
            val answer = CompletableDeferred<Envelope>()
            // The answer can come back before send() returned the id.
            synchronized(waiting) { earlyAnswers.remove(id)?.let { return it } ?: waiting.put(id, answer) }
            return try {
                withTimeoutOrNull(timeout) { answer.await() }
            } finally {
                synchronized(waiting) { waiting.remove(id) }
            }
        }

        /**
         * Opens a file transfer data connection on Wi-Fi, or returns `null` when Wi-Fi is not open:
         * Bluetooth cannot carry files.
         */
        suspend fun openDataConnection(port: Int): DataConnection? =
            sessions.value[LinkPath.WIFI]?.openData?.invoke(port)

        internal fun onSession(
            path: LinkPath,
            session: PcSession?,
        ): Map<LinkPath, PcSession> {
            val now = sessions.updateAndGet { if (session == null) it - path else it + (path to session) }
            _capabilities.value =
                now.values
                    .flatMap { it.helloOrNull()?.capabilities.orEmpty() }
                    .toSet()
            return now
        }

        internal fun clear() {
            sessions.value = emptyMap()
            _capabilities.value = emptySet()
        }

        internal fun deliver(
            envelope: Envelope,
            path: LinkPath,
        ) {
            if (path == LinkPath.WIFI && envelope.reply_to != 0L) {
                val waiter =
                    synchronized(waiting) {
                        waiting.remove(envelope.reply_to) ?: run {
                            earlyAnswers[envelope.reply_to] = envelope
                            if (earlyAnswers.size > EARLY_ANSWERS) earlyAnswers.remove(earlyAnswers.keys.first())
                            null
                        }
                    }
                if (waiter != null) {
                    waiter.complete(envelope)
                    return
                }
            }
            if (!_incoming.tryEmit(
                    envelope,
                )
            ) {
                Log.w(TAG, "Dropped ${envelope.body?.javaClass?.simpleName}, features busy")
            }
        }

        private companion object {
            const val TAG = "FenpoMessenger"
            const val INCOMING_BUFFER = 64
            const val EARLY_ANSWERS = 32
            val REQUEST_TIMEOUT = 15.seconds
            val PREFERENCE = listOf(LinkPath.WIFI, LinkPath.BLUETOOTH)
        }
    }
