package com.fenpo.android.core.data.entitlements

import com.fenpo.protocol.v1.Capability
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Features that might become paid later, through in-app purchases (Play rule: a free app never
 * becomes a paid-upfront app). Everything is free today.
 */
enum class PremiumFeature(
    val capability: Capability,
) {
    CAMERA(Capability.CAPABILITY_CAMERA),
    SCREEN_MIRROR(Capability.CAPABILITY_SCREEN_MIRROR),
    PC_INPUT(Capability.CAPABILITY_PC_INPUT),
    PC_FOLDERS(Capability.CAPABILITY_FILE_PROVIDER),
    PHOTOS(Capability.CAPABILITY_PHOTOS),
    HOTSPOT(Capability.CAPABILITY_HOTSPOT),
    PC_NOTIFICATIONS(Capability.CAPABILITY_PC_NOTIFICATIONS),
}

/**
 * The one place that says whether a feature is available. The phone's `Hello` offers only what is
 * available here, so the PC hides the rest; gating a feature later is a change in this class (and,
 * for users who had Fenpo before, a look at [InstallRecord]).
 */
interface Entitlements {
    fun isAvailable(feature: PremiumFeature): Boolean

    /** [capabilities] without those of features that are not available. */
    fun filter(capabilities: Set<Capability>): Set<Capability> {
        val unavailable =
            PremiumFeature.entries
                .filterNot(::isAvailable)
                .map { it.capability }
                .toSet()
        return capabilities - unavailable
    }
}

/** Fenpo is free: everything is available. */
@Singleton
internal class FreeEntitlements
    @Inject
    constructor() : Entitlements {
        override fun isAvailable(feature: PremiumFeature): Boolean = true
    }
