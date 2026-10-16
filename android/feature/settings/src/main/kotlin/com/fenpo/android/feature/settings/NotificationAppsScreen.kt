package com.fenpo.android.feature.settings

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import androidx.navigation3.runtime.NavKey
import com.fenpo.android.core.data.notifications.NotificationApp
import com.fenpo.android.core.data.notifications.NotificationAppSettings
import com.fenpo.android.core.data.notifications.NotificationAppsRepository
import com.fenpo.android.core.designsystem.component.FenpoListRow
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.component.FileTypeBadge
import com.fenpo.android.core.designsystem.component.NoteBox
import com.fenpo.android.core.designsystem.component.RowDivider
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable
import javax.inject.Inject

/** Navigation key of "Apps that send notifications". */
@Serializable
data object NotificationAppsKey : NavKey

@HiltViewModel
class NotificationAppsViewModel
    @Inject
    constructor(
        private val repository: NotificationAppsRepository,
    ) : ViewModel() {
        val settings: StateFlow<NotificationAppSettings> =
            repository.settings.stateIn(
                viewModelScope,
                SharingStarted.WhileSubscribed(5_000),
                NotificationAppSettings(),
            )

        fun setMirrorNewApps(enabled: Boolean) {
            viewModelScope.launch { repository.setMirrorNewApps(enabled) }
        }

        fun setAppEnabled(
            appId: String,
            enabled: Boolean,
        ) {
            viewModelScope.launch { repository.setAppEnabled(appId, enabled) }
        }
    }

/** "Apps that send notifications" with its view model. */
@Composable
fun NotificationAppsRoute(
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: NotificationAppsViewModel = hiltViewModel(),
) {
    val settings by viewModel.settings.collectAsStateWithLifecycle()
    NotificationAppsScreen(
        settings = settings,
        onBack = onBack,
        onMirrorNewApps = viewModel::setMirrorNewApps,
        onAppEnable = viewModel::setAppEnabled,
        modifier = modifier,
    )
}

/**
 * Apps that send notifications (design: "Bildirim gönderen uygulamalar"): whether new apps are on,
 * then every app seen so far with what the PC can do with it, and the note about one-time codes.
 */
@Composable
fun NotificationAppsScreen(
    settings: NotificationAppSettings,
    onBack: () -> Unit,
    onMirrorNewApps: (Boolean) -> Unit,
    onAppEnable: (String, Boolean) -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        FenpoSubTopBar(title = stringResource(R.string.notification_apps_title), onBack = onBack)
        SwitchRow(
            title = stringResource(R.string.notification_apps_new),
            supporting = stringResource(R.string.notification_apps_new_summary),
            checked = settings.mirrorNewApps,
            onChange = onMirrorNewApps,
        )
        if (settings.apps.isEmpty()) {
            Text(
                text = stringResource(R.string.notification_apps_empty),
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        settings.apps.forEach { app ->
            RowDivider()
            AppRow(app, onAppEnable)
        }
        NoteBox(
            text = stringResource(R.string.notification_apps_otp_note),
            icon = FenpoIcons.Info,
            modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 16.dp),
        )
    }
}

@Composable
private fun AppRow(
    app: NotificationApp,
    onAppEnable: (String, Boolean) -> Unit,
) {
    val colors = MaterialTheme.colorScheme
    val locale = LocalConfiguration.current.locales[0]
    val status =
        when {
            !app.enabled -> R.string.notification_apps_off
            app.replyable -> R.string.notification_apps_replyable
            else -> R.string.notification_apps_shown
        }
    SwitchRow(
        title = app.name,
        supporting = stringResource(status),
        checked = app.enabled,
        onChange = { onAppEnable(app.appId, it) },
        leading = {
            FileTypeBadge(
                label = app.name.take(1).uppercase(locale),
                containerColor = colors.secondaryContainer,
                contentColor = colors.onSecondaryContainer,
            )
        },
    )
}

@Composable
private fun SwitchRow(
    title: String,
    supporting: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit,
    leading: (@Composable () -> Unit)? = null,
) {
    FenpoListRow(
        title = title,
        supporting = supporting,
        leading = leading,
        trailing = { Switch(checked = checked, onCheckedChange = null) },
        modifier = Modifier.selectable(selected = checked, role = Role.Switch) { onChange(!checked) },
    )
}
