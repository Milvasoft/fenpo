package com.fenpo.android.core.connection

import com.fenpo.protocol.v1.Hello
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import javax.inject.Inject
import javax.inject.Singleton

/** State of one connection path to the PC. */
enum class LinkState { OFF, CONNECTING, CONNECTED }

/**
 * What the UI shows about the connection: the two paths and the PC's last `Hello`.
 *
 * @property rssi Last signal strength of the BLE link in dBm, if measured.
 */
data class ConnectionSnapshot(
    val bluetooth: LinkState = LinkState.OFF,
    val wifi: LinkState = LinkState.OFF,
    val remote: Hello? = null,
    val rssi: Int? = null,
) {
    val connected: Boolean get() = bluetooth == LinkState.CONNECTED || wifi == LinkState.CONNECTED
}

/** Live connection state, written by the connection service and read by screens. */
@Singleton
class ConnectionStatus
    @Inject
    constructor() {
        private val _snapshot = MutableStateFlow(ConnectionSnapshot())
        val snapshot: StateFlow<ConnectionSnapshot> = _snapshot.asStateFlow()

        internal fun update(change: (ConnectionSnapshot) -> ConnectionSnapshot) = _snapshot.update(change)
    }
