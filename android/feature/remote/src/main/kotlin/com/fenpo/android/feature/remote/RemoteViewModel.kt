package com.fenpo.android.feature.remote

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.navigation3.runtime.NavKey
import com.fenpo.android.core.connection.PcMessenger
import com.fenpo.protocol.v1.Capability
import com.fenpo.protocol.v1.Envelope
import com.fenpo.protocol.v1.PcButton
import com.fenpo.protocol.v1.PcClick
import com.fenpo.protocol.v1.PcClickKind
import com.fenpo.protocol.v1.PcInput
import com.fenpo.protocol.v1.PcKey
import com.fenpo.protocol.v1.PcKeyCode
import com.fenpo.protocol.v1.PcLaser
import com.fenpo.protocol.v1.PcPresenterAction
import com.fenpo.protocol.v1.PcScroll
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import javax.inject.Inject

/** Navigation key of the touchpad and keyboard. */
@Serializable
data object TouchpadKey : NavKey

/** Navigation key of the presentation remote. */
@Serializable
data object PresenterKey : NavKey

/** Shared by the touchpad and the presentation remote: sends `PcInput` to the PC. */
@HiltViewModel
class RemoteViewModel
    @Inject
    constructor(
        private val messenger: PcMessenger,
    ) : ViewModel() {
        private val sender =
            PcInputSender(viewModelScope) { input ->
                messenger.send(Envelope.Body.PcInput(input), Capability.CAPABILITY_PC_INPUT)
            }

        /** Whether the connected PC takes input. */
        val available: StateFlow<Boolean> =
            messenger.capabilities
                .map { Capability.CAPABILITY_PC_INPUT in it }
                .stateIn(
                    viewModelScope,
                    SharingStarted.WhileSubscribed(5_000),
                    messenger.capabilities.value.contains(Capability.CAPABILITY_PC_INPUT),
                )

        fun move(
            dx: Float,
            dy: Float,
        ) = sender.move(dx * SPEED, dy * SPEED)

        fun click(
            button: PcButton = PcButton.PC_BUTTON_LEFT,
            kind: PcClickKind = PcClickKind.PC_CLICK_KIND_CLICK,
        ) = send(PcInput.Action.Click(PcClick(button = button, kind = kind)))

        /** Two-finger movement in phone pixels, as wheel notches. */
        fun scroll(
            dx: Float,
            dy: Float,
        ) = send(PcInput.Action.Scroll(PcScroll(dx = -dx / NOTCH_PX, dy = -dy / NOTCH_PX)))

        fun key(
            code: PcKeyCode,
            ctrl: Boolean = false,
        ) = send(PcInput.Action.Key(PcKey(code = code, ctrl = ctrl)))

        fun text(text: String) {
            if (text.isNotEmpty()) send(PcInput.Action.Text(text))
        }

        fun presenter(action: PcPresenterAction) = send(PcInput.Action.Presenter(action))

        fun laser(
            active: Boolean,
            x: Float = 0f,
            y: Float = 0f,
        ) = send(PcInput.Action.Laser(PcLaser(active = active, x = x.coerceIn(0f, 1f), y = y.coerceIn(0f, 1f))))

        private var awake = false

        /** Keeps the PC's screen on while the presentation remote is open. */
        fun keepAwake(on: Boolean) {
            awake = on
            send(PcInput.Action.KeepAwake(on))
        }

        private fun send(action: PcInput.Action) = sender.send(PcInput(action = action))

        override fun onCleared() {
            // The view model's scope ends here; turn keep-awake off on a scope that outlives it.
            if (awake) {
                val off = PcInput(action = PcInput.Action.KeepAwake(false))
                CoroutineScope(Dispatchers.Default).launch {
                    messenger.send(Envelope.Body.PcInput(off), Capability.CAPABILITY_PC_INPUT)
                }
            }
            super.onCleared()
        }

        private companion object {
            const val SPEED = 1.6f
            const val NOTCH_PX = 40f
        }
    }
