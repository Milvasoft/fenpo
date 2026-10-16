package com.fenpo.android.core.transport

/**
 * A data channel address of the PC, as listed in the pairing code and in `DeviceState.addresses`.
 *
 * @property host IPv4 or IPv6 literal, without brackets.
 */
public data class Endpoint(
    val host: String,
    val port: Int,
) {
    override fun toString(): String = if (':' in host) "[$host]:$port" else "$host:$port"

    public companion object {
        /** Parses `ip:port` or `[ipv6]:port`, the form [toString] writes; `null` if it is neither. */
        public fun parse(text: String): Endpoint? {
            val host: String
            val portText: String
            if (text.startsWith("[")) {
                val close = text.indexOf("]:")
                if (close < 0) return null
                host = text.substring(1, close)
                portText = text.substring(close + 2)
            } else {
                val colon = text.lastIndexOf(':')
                if (colon <= 0 || text.indexOf(':') != colon) return null
                host = text.substring(0, colon)
                portText = text.substring(colon + 1)
            }
            val port = portText.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
            return Endpoint(host, port)
        }
    }
}
