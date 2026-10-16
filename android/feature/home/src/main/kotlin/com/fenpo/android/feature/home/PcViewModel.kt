package com.fenpo.android.feature.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.android.core.connection.media.MediaBridge
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.LockPc
import com.fenpo.protocol.v1.MediaAction
import com.fenpo.protocol.v1.MediaCommand
import com.fenpo.protocol.v1.MediaState
import com.fenpo.protocol.v1.RingDevice
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import javax.inject.Inject

/**
 * The PC tab: what the PC plays, if it says, and what the phone can ask of it.
 *
 * @property media The PC's player, or `null` when nothing plays there or the PC does not report it.
 */
data class PcTabState(
    val media: MediaState? = null,
    val canRing: Boolean = false,
    val canLock: Boolean = false,
)

/** What a PC tab action came to; the screen turns it into a short message. */
enum class PcActionResult { DONE, NOT_CONNECTED, UNSUPPORTED }

@HiltViewModel
class PcViewModel
    @Inject
    constructor(
        private val messenger: PcMessenger,
        private val mediaBridge: MediaBridge,
    ) : ViewModel() {
        val state: StateFlow<PcTabState> =
            combine(mediaBridge.pcMedia, messenger.capabilities) { media, capabilities ->
                PcTabState(
                    media = media?.takeIf { it.active },
                    canRing = Capability.CAPABILITY_RING in capabilities,
                    canLock = Capability.CAPABILITY_LOCK_PC in capabilities,
                )
            }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), PcTabState())

        /** Makes the PC play a sound until stopped there or after half a minute (device.proto). */
        suspend fun ring(): PcActionResult =
            result(messenger.send(Envelope.Body.Ring(RingDevice()), Capability.CAPABILITY_RING))

        suspend fun lock(): PcActionResult =
            result(messenger.send(Envelope.Body.LockPc(LockPc()), Capability.CAPABILITY_LOCK_PC))

        suspend fun media(action: MediaAction): PcActionResult =
            result(mediaBridge.controlPc(MediaCommand(action = action)))

        /** Volume up or down by a tenth of the PC's scale. */
        suspend fun volume(up: Boolean): PcActionResult {
            val current = state.value.media?.volume_percent ?: return PcActionResult.UNSUPPORTED
            val next = (current + if (up) VOLUME_STEP else -VOLUME_STEP).coerceIn(0, MAX_VOLUME)
            val command = MediaCommand(action = MediaAction.MEDIA_ACTION_SET_VOLUME, volume_percent = next)
            return result(mediaBridge.controlPc(command))
        }

        private fun result(sent: Boolean): PcActionResult =
            when {
                sent -> PcActionResult.DONE
                messenger.connected -> PcActionResult.UNSUPPORTED
                else -> PcActionResult.NOT_CONNECTED
            }

        private companion object {
            const val VOLUME_STEP = 10
            const val MAX_VOLUME = 100
        }
    }
