package com.fenpo.android.core.connection

import android.app.Activity
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.Intent
import android.content.IntentSender
import android.util.Log
import com.fenpo.android.core.ble.GattConnection
import com.fenpo.android.core.data.pc.PairedPcRepository
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * The Bluetooth half of pairing, after the Wi‑Fi half stored the PC: companion device
 * association (step "Bu bilgisayarı tanı"), then a first GATT connection whose encrypted CCCD
 * write makes Android bond with the PC (step "Bluetooth eşleşmesi"), then presence observation and
 * the connection service.
 */
@Singleton
class PcLinkSetup
    @Inject
    constructor(
        @param:ApplicationContext private val context: Context,
        private val association: CompanionAssociation,
        private val pairedPcRepository: PairedPcRepository,
    ) {
        /** Shows the system dialog through [launch] and stores the chosen PC's address. */
        suspend fun associate(
            activity: Activity,
            launch: (IntentSender) -> Unit,
        ): AssociationOutcome {
            val outcome = association.associate(activity, launch)
            if (outcome is AssociationOutcome.Associated) {
                pairedPcRepository.saveBleLink(
                    outcome.associationId,
                    outcome.address,
                )
            }
            return outcome
        }

        /** Passes the companion dialog's activity result (needed on Android 12). */
        fun onDialogResult(
            resultCode: Int,
            data: Intent?,
        ) = association.onDialogResult(resultCode, data)

        /**
         * Connects once in the foreground so Android bonds with the PC (its pairing dialog appears
         * the first time), then lets the connection service take over.
         *
         * @return `true` when the Fenpo service answered with notifications on.
         */
        suspend fun bondAndActivate(
            associationId: Int,
            address: String,
        ): Boolean {
            val device = context.getSystemService(BluetoothManager::class.java).adapter.getRemoteDevice(address)
            val gatt = GattConnection(context, device)
            return try {
                gatt.connect(autoConnect = false)
                true
            } catch (e: Exception) {
                Log.w(TAG, "First BLE connection failed: ${e.message}")
                false
            } finally {
                gatt.close()
            }.also { ready ->
                if (ready) {
                    association.observePresence(associationId, address)
                    ConnectionService.start(context)
                }
            }
        }

        private companion object {
            const val TAG = "FenpoLinkSetup"
        }
    }
