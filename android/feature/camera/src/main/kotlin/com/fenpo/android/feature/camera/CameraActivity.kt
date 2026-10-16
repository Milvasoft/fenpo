package com.fenpo.android.feature.camera

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Bundle
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.WindowManager
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * The camera screen (design: "Kamera modu"), opened from the "Start camera" notification or the
 * home screen. Opening it is the tap Android needs to start the camera service; the stream then
 * keeps going when the screen is left. The screen stays on while visible, and "Dim" turns it black.
 */
@AndroidEntryPoint
class CameraActivity : ComponentActivity() {
    @Inject lateinit var coordinator: CameraCoordinator

    private val permissions =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { granted ->
            if (granted[Manifest.permission.CAMERA] ==
                true
            ) {
                startStream()
            } else {
                finishWith(R.string.camera_permission_needed)
            }
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
            SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        setContent {
            val state by coordinator.ui.collectAsStateWithLifecycle()
            val scope = rememberCoroutineScope()
            LaunchedEffect(state.dimmed) { setDimmed(state.dimmed) }
            FenpoTheme(darkTheme = true) {
                CameraScreen(
                    state = state,
                    onSettingsChange = { scope.launch { coordinator.changeFromPhone(it) } },
                    onDim = coordinator::setDimmed,
                    preview = {
                        AndroidView(factory = { context ->
                            SurfaceView(context).apply { holder.addCallback(previewCallback) }
                        })
                    },
                )
            }
        }
        if (savedInstanceState == null) begin()
        // While this screen is open it is the user's tap Android asks for: answer new PC requests at once.
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.RESUMED) {
                coordinator.requests.drop(1).collect { begin() }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        begin()
    }

    private fun begin() {
        if (coordinator.streaming) return
        if (coordinator.request == null) {
            // Opened from the home screen with no app on the PC asking: nothing to stream yet.
            Toast.makeText(this, R.string.camera_no_request, Toast.LENGTH_LONG).show()
            return
        }
        val wanted =
            buildList {
                add(Manifest.permission.CAMERA)
                if (coordinator.ui.value.settings.microphone) add(Manifest.permission.RECORD_AUDIO)
            }.filter { ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }
        if (wanted.isEmpty()) startStream() else permissions.launch(wanted.toTypedArray())
    }

    private fun startStream() {
        CameraStreamService.start(this)
    }

    private fun finishWith(message: Int) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show()
        finish()
    }

    private fun setDimmed(dimmed: Boolean) {
        window.attributes =
            window.attributes.apply {
                screenBrightness = if (dimmed) DIM_BRIGHTNESS else WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
            }
    }

    private val previewCallback =
        object : SurfaceHolder.Callback {
            override fun surfaceCreated(holder: SurfaceHolder) {
                coordinator.setPreview(holder.surface)
            }

            override fun surfaceChanged(
                holder: SurfaceHolder,
                format: Int,
                width: Int,
                height: Int,
            ) = Unit

            override fun surfaceDestroyed(holder: SurfaceHolder) {
                coordinator.setPreview(null)
            }
        }

    override fun onDestroy() {
        if (isFinishing) coordinator.setDimmed(false)
        super.onDestroy()
    }

    companion object {
        private const val DIM_BRIGHTNESS = 0.01f

        fun intent(context: Context): Intent =
            Intent(
                context,
                CameraActivity::class.java,
            ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
    }
}
