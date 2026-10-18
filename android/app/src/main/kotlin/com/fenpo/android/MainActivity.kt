package com.fenpo.android

import android.graphics.Color
import android.os.Bundle
import android.widget.Toast
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.lifecycleScope
import com.fenpo.android.core.connection.clipboard.ClipboardSync
import com.fenpo.android.core.data.settings.ThemeMode
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.fenpo.android.navigation.FenpoApp
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch
import javax.inject.Inject
import com.fenpo.android.core.connection.R as ConnectionR

/**
 * The only activity. It extends AppCompatActivity so the per-app language API also works below
 * Android 13 and recreates the UI when the language changes (docs/ANDROID.md §17).
 */
@AndroidEntryPoint
class MainActivity : AppCompatActivity() {
    private val viewModel: MainViewModel by viewModels()

    @Inject lateinit var clipboardSync: ClipboardSync

    /**
     * Coming to the front is the one moment Android lets Fenpo read the clipboard: what was copied
     * elsewhere since the last send goes to the PC (docs/ANDROID.md §8).
     */
    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus) return
        lifecycleScope.launch {
            if (clipboardSync.sendIfNew() != null) {
                Toast.makeText(applicationContext, ConnectionR.string.clipboard_sent_auto, Toast.LENGTH_SHORT).show()
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        // Keep the splash until the theme is known, so the first frame is drawn in the right theme.
        installSplashScreen().setKeepOnScreenCondition { viewModel.themeMode.value == null }
        super.onCreate(savedInstanceState)

        setContent {
            val themeMode by viewModel.themeMode.collectAsStateWithLifecycle()
            val darkTheme =
                when (themeMode) {
                    ThemeMode.LIGHT -> false
                    ThemeMode.DARK -> true
                    else -> isSystemInDarkTheme()
                }
            DisposableEffect(darkTheme) {
                val style = SystemBarStyle.auto(Color.TRANSPARENT, Color.TRANSPARENT) { darkTheme }
                enableEdgeToEdge(statusBarStyle = style, navigationBarStyle = style)
                onDispose {}
            }
            FenpoTheme(darkTheme = darkTheme) {
                FenpoApp()
            }
        }
    }
}
