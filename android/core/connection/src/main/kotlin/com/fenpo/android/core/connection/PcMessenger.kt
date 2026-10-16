package com.fenpo.android.core.connection

import android.util.Log
import com.fenpo.android.core.ble.TooLargeForBleException
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.updateAndGet
import javax.inject.Inject
import javax.inject.Singleton

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

        internal fun deliver(envelope: Envelope) {
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
            val PREFERENCE = listOf(LinkPath.WIFI, LinkPath.BLUETOOTH)
        }
    }
