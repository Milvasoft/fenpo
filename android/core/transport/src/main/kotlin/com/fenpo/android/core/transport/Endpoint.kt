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
}
