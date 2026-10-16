package com.fenpo.android.feature.home

import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.connection.ConnectionStatus
import com.fenpo.android.core.connection.LinkState
import com.fenpo.android.core.connection.clipboard.ClipboardSync
import com.fenpo.android.core.connection.notifications.NotificationMirror
import com.fenpo.android.core.connection.transfer.SendResult
import com.fenpo.android.core.connection.transfer.TransferManager
import com.fenpo.android.core.data.notifications.NotificationAppsRepository
import com.fenpo.android.core.data.pc.PairedPcRepository
import com.fenpo.android.core.data.permissions.AppPermission
import com.fenpo.android.core.data.permissions.PermissionChecker
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.data.transfer.TransferHistory
import com.fenpo.android.core.data.transfer.TransferOutcome
import com.fenpo.protocol.v1.DevicePlatform
import com.fenpo.protocol.v1.Hello
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import java.time.Instant
import javax.inject.Inject

/**
 * State for the home and PC screens: the paired PC, the live connection paths and the last
 * clipboard items each way, recent transfers and the notification mirroring summary.
 */
@HiltViewModel
class HomeViewModel
    @Inject
    constructor(
        pairedPcRepository: PairedPcRepository,
        connectionStatus: ConnectionStatus,
        private val clipboardSync: ClipboardSync,
        private val transferManager: TransferManager,
        transferHistory: TransferHistory,
        notificationMirror: NotificationMirror,
        notificationApps: NotificationAppsRepository,
        settingsRepository: SettingsRepository,
        private val permissionChecker: PermissionChecker,
    ) : ViewModel() {
        /** "Notifications go to your PC": shown while mirroring is on and Android lets Fenpo read them. */
        private val mirroring =
            combine(
                settingsRepository.settings,
                notificationApps.settings,
                notificationMirror.last,
            ) { settings, apps, last ->
                if (!settings.features.notificationMirroring ||
                    !permissionChecker.isGranted(AppPermission.NOTIFICATION_ACCESS)
                ) {
                    null
                } else {
                    MirroringSummary(apps.apps.count { it.enabled }, last?.sender, last?.at)
                }
            }

        val uiState: StateFlow<HomeUiState> =
            combine(
                pairedPcRepository.pairedPc,
                connectionStatus.snapshot,
                clipboardSync.toPc,
                clipboardSync.fromPc,
                transferHistory.records,
            ) { pc, connection, toPc, fromPc, records ->
                HomeUiState(
                    pc =
                        pc?.let {
                            PcCardState(
                                name = it.name,
                                osLabel = connection.remote?.osLabel(),
                                connected = connection.connected,
                                bluetooth = connection.bluetooth == LinkState.CONNECTED,
                                wifi = connection.wifi == LinkState.CONNECTED,
                            )
                        },
                    phoneClipboardPreview = toPc,
                    pcClipboardPreview = fromPc,
                    transfers =
                        records
                            .filter { it.outcome == TransferOutcome.COMPLETED }
                            .take(RECENT_TRANSFERS)
                            .map { TransferItem(it.id, it.fileName, it.incoming, it.at) },
                    now = Instant.now(),
                )
            }.combine(mirroring) { state, summary -> state.copy(mirroring = summary) }
                .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), HomeUiState())

        /** "Paste from PC": asks the PC for its clipboard; `false` if it cannot be asked. */
        suspend fun requestPcClipboard(): Boolean = clipboardSync.requestFromPc()

        /** "Send photos" with what the photo picker returned. */
        suspend fun sendPhotos(uris: List<Uri>): SendResult = transferManager.send(uris)

        /** "Windows 11 26300" from Hello; just "Windows" from a PC that does not send the version yet. */
        private fun Hello.osLabel(): String? =
            os_version.ifBlank { if (platform == DevicePlatform.DEVICE_PLATFORM_WINDOWS) WINDOWS else null }

        private companion object {
            const val WINDOWS = "Windows"
            const val RECENT_TRANSFERS = 3
        }
    }
