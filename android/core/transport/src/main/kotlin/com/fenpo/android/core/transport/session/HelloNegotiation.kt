package com.fenpo.android.core.transport.session

import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.Hello

/**
 * This phone as described in its `Hello`.
 *
 * @property id Stable device id (UUID) created at first launch.
 * @property name User visible name, e.g. "Pixel 9".
 * @property appVersion Semantic version of the app.
 * @property capabilities Features this build actually implements; only these are offered.
 * @property osVersion e.g. "Android 12".
 * @property model Hardware model identifier, e.g. "SM-G975F".
 */
public data class LocalDevice(
    val id: String,
    val name: String,
    val appVersion: String,
    val capabilities: Set<Capability>,
    val protocolVersions: List<Int>,
    val osVersion: String = "",
    val model: String = "",
) {
    /** The `Hello` this device sends first on every connection. */
    public fun hello(): Hello =
        Hello(
            device_id = id,
            device_name = name,
            platform = DevicePlatform.DEVICE_PLATFORM_ANDROID,
            app_version = appVersion,
            protocol_versions = protocolVersions,
            capabilities = capabilities.sortedBy { it.value },
            os_version = osVersion,
            model = model,
        )
}

/**
 * Result of the `Hello` exchange: the protocol version both sides use and the features both
 * offer (DECISIONS #11).
 */
public data class NegotiatedSession(
    val protocolVersion: Int,
    val capabilities: Set<Capability>,
    val remote: Hello,
)

/** Negotiates a session from both `Hello`s. */
public object HelloNegotiation {
    /**
     * @return the highest common protocol version and the shared capabilities, or `null` when
     *   the two sides have no protocol version in common.
     */
    public fun negotiate(
        local: Hello,
        remote: Hello,
    ): NegotiatedSession? {
        val version = local.protocol_versions.intersect(remote.protocol_versions.toSet()).maxOrNull() ?: return null
        val capabilities =
            local.capabilities.toSet().intersect(remote.capabilities.toSet()) - Capability.CAPABILITY_UNSPECIFIED
        return NegotiatedSession(version, capabilities, remote)
    }
}
