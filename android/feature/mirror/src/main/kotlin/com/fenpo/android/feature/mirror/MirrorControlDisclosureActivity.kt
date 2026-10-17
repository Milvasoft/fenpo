package com.fenpo.android.feature.mirror

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.fenpo.android.core.data.settings.SettingsRepository
import com.fenpo.android.core.data.settings.ThemeMode
import com.fenpo.android.core.designsystem.theme.FenpoTheme
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.flow.map
import javax.inject.Inject

/**
 * What "Fenpo: control from the PC" does, shown before the user is sent to the accessibility
 * settings (Google Play's prominent disclosure for an accessibility service that is not an
 * accessibility tool). Opened from the screen request's "Allow control".
 */
@AndroidEntryPoint
class MirrorControlDisclosureActivity : ComponentActivity() {
    @Inject lateinit var settingsRepository: SettingsRepository

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val themeMode = settingsRepository.settings.map { it.themeMode }
        setContent {
            val mode by themeMode.collectAsState(ThemeMode.SYSTEM)
            val dark =
                when (mode) {
                    ThemeMode.LIGHT -> false
                    ThemeMode.DARK -> true
                    ThemeMode.SYSTEM -> isSystemInDarkTheme()
                }
            FenpoTheme(darkTheme = dark) {
                ControlDisclosureDialog(
                    onAccept = {
                        startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
                        finish()
                    },
                    onDecline = ::finish,
                )
            }
        }
    }

    companion object {
        fun intent(context: Context): Intent =
            Intent(context, MirrorControlDisclosureActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }
}

/** The disclosure itself: what the service does, when, and that nothing leaves the user's devices. */
@Composable
fun ControlDisclosureDialog(
    onAccept: () -> Unit,
    onDecline: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDecline,
        title = { Text(stringResource(R.string.mirror_disclosure_title)) },
        text = {
            Column {
                Text(stringResource(R.string.mirror_disclosure_what), style = MaterialTheme.typography.bodyMedium)
                Text(
                    stringResource(R.string.mirror_disclosure_data),
                    modifier = Modifier.padding(top = 12.dp),
                    style = MaterialTheme.typography.bodyMedium,
                )
                Text(
                    stringResource(R.string.mirror_disclosure_how),
                    modifier = Modifier.padding(top = 12.dp),
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        },
        confirmButton = { TextButton(onClick = onAccept) { Text(stringResource(R.string.mirror_disclosure_accept)) } },
        dismissButton = {
            TextButton(
                onClick = onDecline,
            ) { Text(stringResource(R.string.mirror_disclosure_decline)) }
        },
    )
}
