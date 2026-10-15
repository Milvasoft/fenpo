package com.fenpo.android.core.protocol

/** Protocol versions this app speaks. Sent in `Hello.protocol_versions`. */
public object ProtocolVersion {
    /** The only version defined so far (`Envelope.version`). */
    public const val V1: Int = 1

    /** Versions offered during the `Hello` exchange, highest first. */
    public val supported: List<Int> = listOf(V1)
}
