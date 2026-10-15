package com.fenpo.android.feature.pairing

import androidx.navigation3.runtime.NavKey
import kotlinx.serialization.Serializable

/** QR scanner, the first pairing step. */
@Serializable
data object ScannerKey : NavKey

/** "Almost done" steps after the code was read. */
@Serializable
data object PairingStepsKey : NavKey

/** Permissions screen, reached from pairing and from Settings. */
@Serializable
data object PermissionsKey : NavKey
