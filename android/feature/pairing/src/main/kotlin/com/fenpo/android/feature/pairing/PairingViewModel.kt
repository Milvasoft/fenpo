package com.fenpo.android.feature.pairing

import android.app.Activity
import android.content.Intent
import android.content.IntentSender
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.connection.AssociationOutcome
import com.fenpo.android.core.connection.PcLinkSetup
import com.fenpo.android.core.data.device.LocalDeviceRepository
import com.fenpo.android.core.data.pc.PairedPc
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.android.core.pairing.PairingClient
import com.fenpo.android.core.pairing.PairingError
import com.fenpo.android.core.pairing.PairingOutcome
import com.fenpo.android.core.pairing.PairingUri
import com.fenpo.android.core.pairing.PairingUriResult
import com.fenpo.android.core.security.IdentityProvider
import com.fenpo.android.core.transport.tls.TlsConnector
import dagger.assisted.Assisted
import dagger.assisted.AssistedFactory
import dagger.assisted.AssistedInject
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * Runs pairing for one scanned code: Wi‑Fi (TLS, `PairingRequest`, `Hello`), then the companion
 * device association, then the Bluetooth bond. Each step starts when the screen asks for it,
 * because the association needs the activity to show the system dialog.
 */
@HiltViewModel(assistedFactory = PairingViewModel.Factory::class)
class PairingViewModel
    @AssistedInject
    constructor(
        @Assisted private val code: String,
        private val identityProvider: IdentityProvider,
        private val localDeviceRepository: LocalDeviceRepository,
        private val pairedPcRepository: PairedPcRepository,
        private val linkSetup: PcLinkSetup,
    ) : ViewModel() {
        private val uri: PairingUri? = (PairingUri.parse(code) as? PairingUriResult.Valid)?.uri

        private val _state =
            MutableStateFlow(
                PairingStepsState(
                    pcName = uri?.pcName.orEmpty(),
                    wifi = StepStatus.ACTIVE,
                    device = StepStatus.QUEUED,
                    bluetooth = StepStatus.QUEUED,
                    permissions = StepStatus.QUEUED,
                    error = if (uri == null) PairingError.UNEXPECTED_RESPONSE else null,
                ),
            )
        val state: StateFlow<PairingStepsState> = _state.asStateFlow()

        private var job: Job? = null

        /** Starts the Wi‑Fi step once; later calls do nothing while it runs or after it succeeded. */
        fun start() {
            val uri = uri ?: return
            if (job?.isActive == true || _state.value.wifi == StepStatus.DONE) return
            _state.update { it.copy(wifi = StepStatus.ACTIVE, error = null) }
            job =
                viewModelScope.launch {
                    val device = localDeviceRepository.localDevice()
                    val client = PairingClient(TlsConnector(identityProvider.identity(device.id)), device)
                    when (val outcome = client.pair(uri)) {
                        is PairingOutcome.Paired -> {
                            // The connection service reconnects on its own; this socket has done its job.
                            outcome.connection.close()
                            val pc = outcome.pc
                            pairedPcRepository.save(
                                PairedPc(
                                    id = pc.pcId,
                                    name = pc.pcName,
                                    certificateSha256 = pc.certificateSha256.hex(),
                                    addresses = pc.addresses.map { it.toString() },
                                ),
                                bleChannelKey = pc.bleChannelKey.toByteArray(),
                            )
                            _state.update {
                                it.copy(
                                    pcName = pc.pcName,
                                    wifi = StepStatus.DONE,
                                    device = StepStatus.ACTIVE,
                                )
                            }
                        }

                        is PairingOutcome.Failed -> {
                            _state.update { it.copy(wifi = StepStatus.FAILED, error = outcome.error) }
                        }
                    }
                }
        }

        /**
         * Runs the association and the bond; [launch] shows the system's companion device dialog.
         * [activity] is only used while that dialog is open (Android 12 needs it).
         */
        fun linkBluetooth(
            activity: Activity,
            launch: (IntentSender) -> Unit,
        ) {
            if (job?.isActive == true || _state.value.device != StepStatus.ACTIVE) return
            _state.update { it.copy(linkError = null) }
            job =
                viewModelScope.launch {
                    val association = linkSetup.associate(activity, launch)
                    if (association !is AssociationOutcome.Associated) {
                        _state.update { it.copy(device = StepStatus.FAILED, linkError = LinkError.NOT_ASSOCIATED) }
                        return@launch
                    }
                    _state.update { it.copy(device = StepStatus.DONE, bluetooth = StepStatus.ACTIVE) }
                    bond(association.associationId, association.address)
                }
        }

        /** The companion dialog closed; a cancel fails the step through [linkBluetooth]. */
        fun onCompanionDialogResult(
            resultCode: Int,
            data: Intent?,
        ) = linkSetup.onDialogResult(resultCode, data)

        /** Tries the failed Bluetooth step again. */
        fun retryLink() {
            val current = _state.value
            when {
                current.device == StepStatus.FAILED -> {
                    _state.update { it.copy(device = StepStatus.ACTIVE, linkError = null) }
                }

                current.bluetooth == StepStatus.FAILED -> {
                    job =
                        viewModelScope.launch {
                            val pc = pairedPcRepository.pairedPc.first() ?: return@launch
                            _state.update { it.copy(bluetooth = StepStatus.ACTIVE, linkError = null) }
                            bond(pc.associationId, pc.bleAddress)
                        }
                }
            }
        }

        private suspend fun bond(
            associationId: Int,
            address: String,
        ) {
            if (linkSetup.bondAndActivate(associationId, address)) {
                _state.update { it.copy(bluetooth = StepStatus.DONE, permissions = StepStatus.ACTIVE, paired = true) }
            } else {
                _state.update { it.copy(bluetooth = StepStatus.FAILED, linkError = LinkError.BLUETOOTH_FAILED) }
            }
        }

        @AssistedFactory
        interface Factory {
            fun create(code: String): PairingViewModel
        }
    }
