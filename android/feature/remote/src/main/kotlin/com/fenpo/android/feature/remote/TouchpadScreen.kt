package com.fenpo.android.feature.remote

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.input.pointer.PointerInputScope
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.component.NoteBox
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.protocol.v1.PcButton
import com.fenpo.protocol.v1.PcClickKind
import com.fenpo.protocol.v1.PcKeyCode
import com.fenpo.android.core.connection.R as ConnectionR

/** Touchpad and keyboard with its view model. */
@Composable
fun TouchpadRoute(
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: RemoteViewModel = hiltViewModel(),
) {
    val available by viewModel.available.collectAsStateWithLifecycle()
    TouchpadScreen(
        available = available,
        onBack = onBack,
        pad =
            TouchpadActions(
                move = viewModel::move,
                click = { viewModel.click() },
                rightClick = { viewModel.click(PcButton.PC_BUTTON_RIGHT) },
                press = { viewModel.click(kind = PcClickKind.PC_CLICK_KIND_PRESS) },
                release = { viewModel.click(kind = PcClickKind.PC_CLICK_KIND_RELEASE) },
                scroll = viewModel::scroll,
            ),
        onText = viewModel::text,
        onKey = { viewModel.key(it) },
        modifier = modifier,
    )
}

/** What the touch surface does with the finger. */
class TouchpadActions(
    val move: (Float, Float) -> Unit = { _, _ -> },
    val click: () -> Unit = {},
    val rightClick: () -> Unit = {},
    val press: () -> Unit = {},
    val release: () -> Unit = {},
    val scroll: (Float, Float) -> Unit = { _, _ -> },
)

/**
 * The phone as the PC's touchpad and keyboard (pc_input.proto). The design does not draw it; it
 * uses the app's sub-screen bar, card shapes and buttons. One finger moves and taps, two fingers
 * scroll or right-click, a long press starts a drag; the field below types on the PC.
 */
@Composable
fun TouchpadScreen(
    available: Boolean,
    onBack: () -> Unit,
    onText: (String) -> Unit,
    onKey: (PcKeyCode) -> Unit,
    modifier: Modifier = Modifier,
    pad: TouchpadActions = TouchpadActions(),
) {
    Column(modifier = modifier.fillMaxSize().imePadding()) {
        FenpoSubTopBar(title = stringResource(R.string.touchpad_title), onBack = onBack)
        if (!available) {
            NoteBox(
                text = stringResource(ConnectionR.string.pc_update_needed),
                icon = FenpoIcons.Info,
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
            )
        }
        Box(
            modifier =
                Modifier
                    .weight(1f)
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp)
                    .clip(RoundedCornerShape(24.dp))
                    .background(MaterialTheme.colorScheme.surfaceContainerHigh)
                    .pointerInput(Unit) { touchpad(pad) },
            contentAlignment = Alignment.Center,
        ) {
            Text(
                stringResource(R.string.touchpad_hint),
                modifier = Modifier.padding(32.dp),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            FilledTonalButton(
                onClick = pad.click,
                modifier = Modifier.weight(1f),
            ) { Text(stringResource(R.string.touchpad_left)) }
            FilledTonalButton(onClick = pad.rightClick, modifier = Modifier.weight(1f)) {
                Text(stringResource(R.string.touchpad_right))
            }
        }
        KeyRow(onKey)
        TypingField(onText = onText, onKey = onKey)
    }
}

@Composable
private fun KeyRow(onKey: (PcKeyCode) -> Unit) {
    val keys =
        listOf(
            PcKeyCode.PC_KEY_CODE_ESCAPE to R.string.key_esc,
            PcKeyCode.PC_KEY_CODE_TAB to R.string.key_tab,
            PcKeyCode.PC_KEY_CODE_LEFT to R.string.key_left,
            PcKeyCode.PC_KEY_CODE_UP to R.string.key_up,
            PcKeyCode.PC_KEY_CODE_DOWN to R.string.key_down,
            PcKeyCode.PC_KEY_CODE_RIGHT to R.string.key_right,
            PcKeyCode.PC_KEY_CODE_BACKSPACE to R.string.key_backspace,
            PcKeyCode.PC_KEY_CODE_ENTER to R.string.key_enter,
            PcKeyCode.PC_KEY_CODE_WINDOWS to R.string.key_windows,
        )
    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .horizontalScroll(
                    rememberScrollState(),
                ).padding(horizontal = 16.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        keys.forEach { (code, label) -> FilledTonalButton(onClick = { onKey(code) }) { Text(stringResource(label)) } }
    }
}

/** Typing goes out as it happens: added characters as text, removed ones as Backspace. */
@Composable
private fun TypingField(
    onText: (String) -> Unit,
    onKey: (PcKeyCode) -> Unit,
) {
    var value by remember { mutableStateOf("") }
    OutlinedTextField(
        value = value,
        onValueChange = { next ->
            val common = value.commonPrefixWith(next).length
            repeat(value.length - common) { onKey(PcKeyCode.PC_KEY_CODE_BACKSPACE) }
            onText(next.substring(common))
            value = next.takeLast(KEEP_CHARS)
        },
        modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 16.dp),
        placeholder = { Text(stringResource(R.string.touchpad_type)) },
        singleLine = true,
    )
}

/** Gestures of the touch surface; see [TouchpadScreen]. */
private suspend fun PointerInputScope.touchpad(actions: TouchpadActions) {
    val slop = viewConfiguration.touchSlop
    awaitEachGesture {
        val down = awaitFirstDown(requireUnconsumed = false)
        var maxPointers = 1
        var moved = false
        var dragging = false
        while (true) {
            val waitingForHold = !moved && !dragging && maxPointers == 1
            var next =
                if (waitingForHold) {
                    withTimeoutOrNull(
                        LONG_PRESS_MS,
                    ) { awaitPointerEvent() }
                } else {
                    awaitPointerEvent()
                }
            if (next == null) {
                // Held still: a drag starts.
                dragging = true
                actions.press()
                next = awaitPointerEvent()
            }
            val event = next
            val pressed = event.changes.filter { it.pressed }
            if (pressed.isEmpty()) break
            maxPointers = maxOf(maxPointers, pressed.size)
            if (pressed.size == 1 && maxPointers == 1) {
                val change = pressed.first()
                if (!moved && (change.position - down.position).getDistance() > slop) moved = true
                if (moved || dragging) {
                    val delta = change.positionChange()
                    actions.move(delta.x, delta.y)
                }
            } else if (pressed.size >= 2) {
                val delta = pressed.fold(Offset.Zero) { sum, c -> sum + c.positionChange() } / pressed.size.toFloat()
                if (delta.getDistance() > 0f) {
                    moved = true
                    actions.scroll(delta.x, delta.y)
                }
            }
            event.changes.forEach { it.consume() }
        }
        when {
            dragging -> actions.release()
            moved -> Unit
            maxPointers >= 2 -> actions.rightClick()
            else -> actions.click()
        }
    }
}

private const val LONG_PRESS_MS = 450L
private const val KEEP_CHARS = 40
