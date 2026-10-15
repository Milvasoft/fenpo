package com.fenpo.android.feature.pairing

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
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
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * Runs pairing for one scanned code and exposes the steps screen state. Pairing starts when the
 * screen calls [start], after the local network permission (Android 17) is settled.
 *
 * Phase C so far covers the Wi‑Fi step. The companion device and Bluetooth steps come with the
 * BLE channel; until then they stay "Up next" and the flow continues with permissions.
 */
@HiltViewModel(assistedFactory = PairingViewModel.Factory::class)
class PairingViewModel
    @AssistedInject
    constructor(
        @Assisted private val code: String,
        private val identityProvider: IdentityProvider,
        private val localDeviceRepository: LocalDeviceRepository,
        private val pairedPcRepository: PairedPcRepository,
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

        /** Starts pairing once; later calls do nothing while it runs or after it succeeded. */
        fun start() {
            val uri = uri ?: return
            if (job?.isActive == true || _state.value.paired) return
            _state.update { it.copy(wifi = StepStatus.ACTIVE, error = null) }
            job =
                viewModelScope.launch {
                    val device = localDeviceRepository.localDevice()
                    val client = PairingClient(TlsConnector(identityProvider.identity(device.id)), device)
                    when (val outcome = client.pair(uri)) {
                        is PairingOutcome.Paired -> {
                            // No connection service yet (phase C, BLE part): keep the pairing, close the socket.
                            outcome.connection.close()
                            val pc = outcome.pc
                            pairedPcRepository.save(
                                PairedPc(
                                    id = pc.pcId,
                                    name = pc.pcName,
                                    certificateSha256 = pc.certificateSha256.hex(),
                                    addresses = pc.addresses.map { it.toString() },
                                    bleServiceUuid = pc.bleServiceUuid,
                                ),
                                bleChannelKey = pc.bleChannelKey.toByteArray(),
                            )
                            _state.update {
                                it.copy(
                                    pcName = pc.pcName,
                                    wifi = StepStatus.DONE,
                                    permissions = StepStatus.ACTIVE,
                                    paired = true,
                                )
                            }
                        }

                        is PairingOutcome.Failed -> {
                            _state.update { it.copy(wifi = StepStatus.FAILED, error = outcome.error) }
                        }
                    }
                }
        }

        @AssistedFactory
        interface Factory {
            fun create(code: String): PairingViewModel
        }
    }
