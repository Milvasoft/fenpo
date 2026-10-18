package com.fenpo.android.core.connection

import android.annotation.SuppressLint
import android.app.Activity
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.companion.AssociationInfo
import android.companion.AssociationRequest
import android.companion.BluetoothLeDeviceFilter
import android.companion.CompanionDeviceManager
import android.companion.ObservingDevicePresenceRequest
import android.content.Context
import android.content.Intent
import android.content.IntentSender
import android.os.Build
import android.os.ParcelUuid
import android.util.Log
import androidx.annotation.RequiresApi
import com.fenpo.android.core.transport.ble.FenpoBleUuids
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CancellableContinuation
import kotlinx.coroutines.suspendCancellableCoroutine
import java.util.regex.Pattern
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.coroutines.resume

/** Result of [CompanionAssociation.associate]. */
sealed interface AssociationOutcome {
    /**
     * The user picked the PC.
     *
     * @property associationId CDM association id; 0 on Android 12, which has none.
     * @property address The PC's Bluetooth address as CDM saw it.
     */
    data class Associated(
        val associationId: Int,
        val address: String,
    ) : AssociationOutcome

    /** The user cancelled or no Fenpo PC was found; [reason] is for logs. */
    data class Failed(
        val reason: String,
    ) : AssociationOutcome
}

/**
 * Registers the PC with CompanionDeviceManager (docs/ANDROID.md §1): profile-less, single-device
 * BLE association filtered by the fixed Fenpo service UUID and the PC's name. Every Fenpo PC
 * advertises the same service, so with two PCs in a house the service alone picked whichever came
 * first (device report from the iPhone, 18 October); the PC's Bluetooth name is its computer
 * name, the same one its QR code carries. The association lets Fenpo start its
 * connection service from the background, exempts it from battery optimization and wakes it when
 * the PC comes into range.
 *
 * Android 13+ reports the result through a callback with an [AssociationInfo]; Android 12 only
 * through the dialog's activity result, so the screen must pass that to [onDialogResult].
 */
@Singleton
class CompanionAssociation
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
    ) {
        private val manager: CompanionDeviceManager = context.getSystemService(CompanionDeviceManager::class.java)
        private var pending: CancellableContinuation<AssociationOutcome>? = null

        /**
         * Shows the system dialog (through [launch]) and waits for the user's choice.
         *
         * @param activity The visible activity. Android 12 requires the manager to come from an
         *   Activity context (it casts it); it is only held while the dialog is open.
         * @param launch Starts the dialog's IntentSender with an activity result launcher whose
         *   result goes to [onDialogResult].
         */
        suspend fun associate(
            activity: Activity,
            pcName: String,
            launch: (IntentSender) -> Unit,
        ): AssociationOutcome =
            suspendCancellableCoroutine { continuation ->
                pending = continuation
                continuation.invokeOnCancellation { pending = null }
                val filter =
                    BluetoothLeDeviceFilter
                        .Builder()
                        .setScanFilter(ScanFilter.Builder().setServiceUuid(ParcelUuid(FenpoBleUuids.SERVICE)).build())
                if (pcName.isNotBlank()) {
                    filter.setNamePattern(
                        Pattern.compile("^" + Pattern.quote(pcName.trim()) + "$", Pattern.CASE_INSENSITIVE),
                    )
                }
                val request =
                    AssociationRequest
                        .Builder()
                        .addDeviceFilter(filter.build())
                        .setSingleDevice(true)
                        .build()
                val activityManager = activity.getSystemService(CompanionDeviceManager::class.java)
                if (Build.VERSION.SDK_INT >= ANDROID_13) {
                    activityManager.associate(request, context.mainExecutor, callback(launch))
                } else {
                    @Suppress("DEPRECATION")
                    activityManager.associate(request, callback(launch), null)
                }
            }

        /** Passes the dialog's activity result; required on Android 12, harmless on 13+. */
        fun onDialogResult(
            resultCode: Int,
            data: Intent?,
        ) {
            if (resultCode != Activity.RESULT_OK) return finish(AssociationOutcome.Failed("Dialog closed"))
            if (Build.VERSION.SDK_INT >= ANDROID_13) return // onAssociationCreated delivers the result.
            @Suppress("DEPRECATION")
            val scan = data?.getParcelableExtra<ScanResult>(CompanionDeviceManager.EXTRA_DEVICE)
            val address = scan?.device?.address?.uppercase()
            Log.i(TAG, "Associated (Android 12) at $address")
            finish(
                if (address ==
                    null
                ) {
                    AssociationOutcome.Failed("No device in result")
                } else {
                    AssociationOutcome.Associated(0, address)
                },
            )
        }

        private fun callback(launch: (IntentSender) -> Unit) =
            object : CompanionDeviceManager.Callback() {
                // Android 13+.
                override fun onAssociationPending(intentSender: IntentSender) = launch(intentSender)

                // Android 12.
                @Deprecated("Replaced by onAssociationPending on Android 13")
                override fun onDeviceFound(intentSender: IntentSender) {
                    if (Build.VERSION.SDK_INT < ANDROID_13) launch(intentSender)
                }

                @RequiresApi(ANDROID_13)
                override fun onAssociationCreated(associationInfo: AssociationInfo) {
                    val address = associationInfo.deviceMacAddress?.toString()?.uppercase()
                    Log.i(TAG, "Associated ${associationInfo.id} at $address")
                    finish(
                        if (address == null) {
                            AssociationOutcome.Failed("No address")
                        } else {
                            AssociationOutcome.Associated(associationInfo.id, address)
                        },
                    )
                }

                override fun onFailure(error: CharSequence?) {
                    Log.w(TAG, "Association failed: $error")
                    finish(AssociationOutcome.Failed(error?.toString().orEmpty()))
                }
            }

        private fun finish(outcome: AssociationOutcome) {
            val continuation = pending ?: return
            pending = null
            if (continuation.isActive) continuation.resume(outcome)
        }

        /** Wakes Fenpo through [FenpoCompanionService] when the PC appears or disappears. */
        @SuppressLint("MissingPermission")
        fun observePresence(
            associationId: Int,
            address: String,
        ) {
            if (Build.VERSION.SDK_INT >= ANDROID_16) {
                manager.startObservingDevicePresence(
                    ObservingDevicePresenceRequest.Builder().setAssociationId(associationId).build(),
                )
            } else {
                @Suppress("DEPRECATION")
                manager.startObservingDevicePresence(address)
            }
        }

        /** Removes the association, e.g. when the user unpairs the PC. */
        fun forget(
            associationId: Int,
            address: String,
        ) {
            if (Build.VERSION.SDK_INT >= ANDROID_13 && associationId != 0) {
                manager.disassociate(associationId)
            } else {
                @Suppress("DEPRECATION")
                manager.disassociate(address)
            }
        }

        /** The PCs this app is associated with, for logs: `id@address` on 13+, the address on 12. */
        fun associations(): List<String> =
            if (Build.VERSION.SDK_INT >= ANDROID_13) {
                manager.myAssociations.map { "${it.id}@${it.deviceMacAddress}" }
            } else {
                @Suppress("DEPRECATION")
                manager.associations
            }

        private companion object {
            const val TAG = "FenpoCdm"
            const val ANDROID_13 = 33
            const val ANDROID_16 = 36
        }
    }
