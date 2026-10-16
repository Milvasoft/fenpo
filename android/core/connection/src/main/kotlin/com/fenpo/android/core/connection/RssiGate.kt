package com.fenpo.android.core.connection

/**
 * When to report the PC's signal (protocol/README "Proximity"): on a change of at least 4 dB, or
 * every 30 seconds so the PC knows the phone is still there.
 */
internal class RssiGate(
    private val minChangeDb: Int = 4,
    private val refreshMs: Long = 30_000,
) {
    private var last: Int? = null
    private var lastAt = 0L

    fun accept(
        dbm: Int,
        nowMs: Long,
    ): Boolean {
        val previous = last
        val due = previous == null || kotlin.math.abs(dbm - previous) >= minChangeDb || nowMs - lastAt >= refreshMs
        if (due) {
            last = dbm
            lastAt = nowMs
        }
        return due
    }
}
