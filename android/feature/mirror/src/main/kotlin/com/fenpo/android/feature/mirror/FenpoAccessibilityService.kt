package com.fenpo.android.feature.mirror

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.graphics.Path
import android.os.Bundle
import android.util.Log
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import com.fenpo.protocol.v1.ScreenGlobalAction
import com.fenpo.protocol.v1.ScreenInput
import com.fenpo.protocol.v1.ScreenPoint

/**
 * Control of the mirrored screen from the PC (docs/ANDROID.md §11): gestures with
 * `dispatchGesture`, Back/Home/Recents with `performGlobalAction`, text into the focused field.
 * Fenpo is not an accessibility tool (`isAccessibilityTool=false`); the user turns this on only
 * to control the phone from the PC, and it does nothing unless a mirroring session sends input.
 */
class FenpoAccessibilityService : AccessibilityService() {
    override fun onServiceConnected() {
        super.onServiceConnected()
        running = this
        Log.i(TAG, "Remote control available")
    }

    override fun onUnbind(intent: android.content.Intent?): Boolean {
        if (running === this) running = null
        return super.onUnbind(intent)
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit

    override fun onInterrupt() = Unit

    /** Carries out one input; `false` when Android refused it. */
    fun perform(input: ScreenInput): Boolean {
        val (width, height) = screenSize()

        fun at(point: ScreenPoint) = point.x.coerceIn(0f, 1f) * width to point.y.coerceIn(0f, 1f) * height
        return when (val action = input.action) {
            is ScreenInput.Action.Tap -> {
                stroke(listOf(at(action.value)), TAP_MS)
            }

            is ScreenInput.Action.LongPress -> {
                stroke(listOf(at(action.value)), LONG_PRESS_MS)
            }

            is ScreenInput.Action.Swipe -> {
                val from = action.value.from ?: return false
                val to = action.value.to ?: return false
                stroke(
                    listOf(at(from), at(to)),
                    action.value.duration_ms
                        .toLong()
                        .coerceIn(MIN_SWIPE_MS, MAX_SWIPE_MS),
                )
            }

            is ScreenInput.Action.Global -> {
                global(action.value)
            }

            is ScreenInput.Action.Text -> {
                type(action.value)
            }

            null -> {
                false
            }
        }
    }

    private fun stroke(
        points: List<Pair<Float, Float>>,
        durationMs: Long,
    ): Boolean {
        val path =
            Path().apply {
                moveTo(points.first().first, points.first().second)
                points.drop(1).forEach { lineTo(it.first, it.second) }
            }
        val gesture =
            GestureDescription
                .Builder()
                .addStroke(
                    GestureDescription.StrokeDescription(path, 0, durationMs),
                ).build()
        return dispatchGesture(gesture, null, null)
    }

    private fun global(action: ScreenGlobalAction): Boolean =
        performGlobalAction(
            when (action) {
                ScreenGlobalAction.SCREEN_GLOBAL_ACTION_BACK -> GLOBAL_ACTION_BACK
                ScreenGlobalAction.SCREEN_GLOBAL_ACTION_HOME -> GLOBAL_ACTION_HOME
                ScreenGlobalAction.SCREEN_GLOBAL_ACTION_RECENTS -> GLOBAL_ACTION_RECENTS
                ScreenGlobalAction.SCREEN_GLOBAL_ACTION_NOTIFICATIONS -> GLOBAL_ACTION_NOTIFICATIONS
                ScreenGlobalAction.SCREEN_GLOBAL_ACTION_QUICK_SETTINGS -> GLOBAL_ACTION_QUICK_SETTINGS
                else -> return false
            },
        )

    /** Appends [text] to the focused field, keeping what is already there. */
    private fun type(text: String): Boolean {
        val field = rootInActiveWindow?.findFocus(AccessibilityNodeInfo.FOCUS_INPUT) ?: return false
        val current =
            field.text
                ?.takeUnless { field.isShowingHintText }
                ?.toString()
                .orEmpty()
        val arguments =
            Bundle().apply {
                putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, current + text)
            }
        return field.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, arguments)
    }

    /** The whole display in its current rotation, the frame the PC's fractions refer to. */
    private fun screenSize(): Pair<Float, Float> {
        val bounds = getSystemService(WindowManager::class.java).maximumWindowMetrics.bounds
        return bounds.width().toFloat() to bounds.height().toFloat()
    }

    companion object {
        private const val TAG = "FenpoMirror"
        private const val TAP_MS = 50L
        private const val LONG_PRESS_MS = 700L
        private const val MIN_SWIPE_MS = 50L
        private const val MAX_SWIPE_MS = 3_000L

        /** The connected service, or `null` while the user has not turned it on. */
        @Volatile var running: FenpoAccessibilityService? = null
            private set
    }
}
