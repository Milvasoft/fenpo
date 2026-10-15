package com.fenpo.android

import android.graphics.Color
import android.os.Bundle
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
import com.fenpo.android.core.data.settings.ThemeMode
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import com.fenpo.android.navigation.FenpoApp
import dagger.hilt.android.AndroidEntryPoint

/**
 * The only activity. It extends AppCompatActivity so the per-app language API also works below
 * Android 13 and recreates the UI when the language changes (docs/ANDROID.md §17).
 */
@AndroidEntryPoint
class MainActivity : AppCompatActivity() {
    private val viewModel: MainViewModel by viewModels()

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
