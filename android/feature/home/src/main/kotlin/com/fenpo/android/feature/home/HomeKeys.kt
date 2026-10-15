package com.fenpo.android.feature.home

import androidx.navigation3.runtime.NavKey
import kotlinx.serialization.Serializable

/** Navigation key of the Fenpo (home) tab. */
@Serializable
data object HomeKey : NavKey

/** Navigation key of the PC tab. */
@Serializable
data object PcKey : NavKey
