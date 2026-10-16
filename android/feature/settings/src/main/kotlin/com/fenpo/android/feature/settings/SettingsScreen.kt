package com.fenpo.android.feature.settings

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Badge
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import androidx.core.app.LocaleManagerCompat
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.data.settings.AppLanguage
import com.fenpo.android.core.data.settings.Feature
import com.fenpo.android.core.data.settings.ThemeMode
import com.fenpo.android.core.designsystem.component.FenpoListRow
import com.fenpo.android.core.designsystem.component.FenpoRootTopBar
import com.fenpo.android.core.designsystem.component.LeadingIcon
import com.fenpo.android.core.designsystem.component.SectionLabel
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import java.util.Locale

/** Settings tab with its view model. */
@Composable
fun SettingsRoute(
    onOpenPermissions: () -> Unit,
    onOpenNotificationApps: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: SettingsViewModel = hiltViewModel(),
) {
    val state by viewModel.uiState.collectAsStateWithLifecycle()
    LifecycleResumeEffect(Unit) {
        viewModel.refresh()
        onPauseOrDispose {}
    }
    val context = LocalContext.current
    val systemLanguage = remember(context) { systemLanguageName(LocaleManagerCompat.getSystemLocales(context).get(0)) }
    SettingsScreen(
        state = state,
        systemLanguageName = systemLanguage,
        onThemeModeChange = viewModel::setThemeMode,
        onLanguageChange = viewModel::setLanguage,
        onFeatureChange = viewModel::setFeatureEnabled,
        onOpenPermissions = onOpenPermissions,
        onOpenNotificationApps = onOpenNotificationApps,
        modifier = modifier,
    )
}

/**
 * Settings (design: "Ayarlar"): appearance and language, feature switches and missing
 * permissions. Feature switches are stored now and take effect as each feature is built.
 *
 * @param systemLanguageName Name of the system language in itself, shown as "System language (Türkçe)".
 * @param showLanguageDialog Opens the language dialog initially; used by screenshot tests.
 */
@Composable
fun SettingsScreen(
    state: SettingsUiState,
    systemLanguageName: String,
    onThemeModeChange: (ThemeMode) -> Unit,
    onLanguageChange: (AppLanguage) -> Unit,
    onFeatureChange: (Feature, Boolean) -> Unit,
    onOpenPermissions: () -> Unit,
    modifier: Modifier = Modifier,
    onOpenNotificationApps: () -> Unit = {},
    showLanguageDialog: Boolean = false,
) {
    var languageDialog by rememberSaveable { mutableStateOf(showLanguageDialog) }
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState()),
    ) {
        FenpoRootTopBar(title = stringResource(R.string.settings_title))

        SectionLabel(stringResource(R.string.section_appearance_language))
        Row(modifier = Modifier.padding(start = 16.dp, end = 24.dp, top = 12.dp, bottom = 4.dp)) {
            LeadingIcon(FenpoIcons.Palette, Modifier.padding(top = 2.dp, end = 16.dp))
            Column(Modifier.weight(1f)) {
                Text(stringResource(R.string.appearance), style = MaterialTheme.typography.bodyLarge)
                ThemeSelector(
                    selected = state.themeMode,
                    onSelect = onThemeModeChange,
                    modifier = Modifier.padding(top = 10.dp),
                )
            }
        }
        FenpoListRow(
            title = stringResource(R.string.language),
            supporting = languageLabel(state.language, systemLanguageName),
            leading = { LeadingIcon(FenpoIcons.Globe) },
            modifier = Modifier.clickable { languageDialog = true },
        )

        SectionLabel(stringResource(R.string.section_features))
        // Tapping the row opens the app list (design: "12 uygulama seçili"); the switch turns it all off.
        val checked = state.features.notificationMirroring
        FenpoListRow(
            title = stringResource(R.string.feature_notifications),
            supporting =
                state.mirroredApps?.let {
                    pluralStringResource(R.plurals.feature_notifications_apps, it, it)
                },
            leading = { LeadingIcon(FenpoIcons.Bell) },
            trailing = {
                Switch(
                    checked = checked,
                    onCheckedChange = { onFeatureChange(Feature.NOTIFICATION_MIRRORING, it) },
                )
            },
            modifier = Modifier.clickable(onClick = onOpenNotificationApps),
        )
        FeatureRow(
            Feature.MESSAGE_SYNC,
            FenpoIcons.Message,
            R.string.feature_messages,
            R.string.feature_messages_summary,
            state,
            onFeatureChange,
        )
        FeatureRow(
            Feature.CLIPBOARD_SHARING,
            FenpoIcons.Clipboard,
            R.string.feature_clipboard,
            null,
            state,
            onFeatureChange,
        )
        FeatureRow(
            Feature.SCREENSHOT_SHARING,
            FenpoIcons.Screenshot,
            R.string.feature_screenshots,
            null,
            state,
            onFeatureChange,
        )
        FeatureRow(
            Feature.DO_NOT_DISTURB_SYNC,
            FenpoIcons.DoNotDisturb,
            R.string.feature_dnd,
            R.string.feature_dnd_summary,
            state,
            onFeatureChange,
        )

        SectionLabel(stringResource(R.string.section_permissions))
        FenpoListRow(
            title = stringResource(R.string.permissions),
            supporting =
                if (state.missingPermissions == 0) {
                    stringResource(R.string.permissions_all_granted)
                } else {
                    pluralStringResource(
                        R.plurals.permissions_missing,
                        state.missingPermissions,
                        state.missingPermissions,
                    )
                },
            leading = { LeadingIcon(FenpoIcons.Info) },
            trailing = {
                if (state.missingPermissions > 0) {
                    Badge(
                        containerColor = MaterialTheme.colorScheme.error,
                    ) { Text(state.missingPermissions.toString()) }
                }
            },
            modifier = Modifier.clickable(onClick = onOpenPermissions),
        )
    }

    if (languageDialog) {
        LanguageDialog(
            current = state.language,
            systemLanguageName = systemLanguageName,
            onDismiss = { languageDialog = false },
            onApply = {
                languageDialog = false
                onLanguageChange(it)
            },
        )
    }
}

@Composable
private fun ThemeSelector(
    selected: ThemeMode,
    onSelect: (ThemeMode) -> Unit,
    modifier: Modifier = Modifier,
) {
    val options =
        listOf(
            ThemeMode.SYSTEM to R.string.theme_system,
            ThemeMode.LIGHT to R.string.theme_light,
            ThemeMode.DARK to R.string.theme_dark,
        )
    SingleChoiceSegmentedButtonRow(modifier = modifier.fillMaxWidth()) {
        options.forEachIndexed { index, (mode, label) ->
            SegmentedButton(
                selected = mode == selected,
                onClick = { onSelect(mode) },
                shape = SegmentedButtonDefaults.itemShape(index, options.size),
            ) {
                Text(stringResource(label))
            }
        }
    }
}

@Composable
private fun FeatureRow(
    feature: Feature,
    icon: Int,
    title: Int,
    summary: Int?,
    state: SettingsUiState,
    onFeatureChange: (Feature, Boolean) -> Unit,
) {
    val checked =
        with(state.features) {
            when (feature) {
                Feature.NOTIFICATION_MIRRORING -> notificationMirroring
                Feature.MESSAGE_SYNC -> messageSync
                Feature.CLIPBOARD_SHARING -> clipboardSharing
                Feature.SCREENSHOT_SHARING -> screenshotSharing
                Feature.DO_NOT_DISTURB_SYNC -> doNotDisturbSync
            }
        }
    FenpoListRow(
        title = stringResource(title),
        supporting = summary?.let { stringResource(it) },
        leading = { LeadingIcon(icon) },
        trailing = { Switch(checked = checked, onCheckedChange = null) },
        modifier =
            Modifier.selectable(selected = checked, role = Role.Switch) { onFeatureChange(feature, !checked) },
    )
}

@Composable
private fun LanguageDialog(
    current: AppLanguage,
    systemLanguageName: String,
    onDismiss: () -> Unit,
    onApply: (AppLanguage) -> Unit,
) {
    var choice by rememberSaveable { mutableStateOf(current) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.language)) },
        text = {
            Column {
                AppLanguage.entries.forEach { language ->
                    Row(
                        modifier =
                            Modifier
                                .fillMaxWidth()
                                .selectable(
                                    selected = language == choice,
                                    role = Role.RadioButton,
                                ) { choice = language }
                                .padding(vertical = 12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        RadioButton(selected = language == choice, onClick = null)
                        Text(
                            text = languageLabel(language, systemLanguageName),
                            modifier = Modifier.padding(start = 16.dp),
                            style = MaterialTheme.typography.bodyLarge,
                        )
                    }
                }
                Text(
                    text = stringResource(R.string.language_dialog_note),
                    modifier = Modifier.padding(top = 12.dp),
                    style = MaterialTheme.typography.bodyMedium,
                )
            }
        },
        confirmButton = { TextButton(onClick = { onApply(choice) }) { Text(stringResource(R.string.apply)) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

@Composable
private fun languageLabel(
    language: AppLanguage,
    systemLanguageName: String,
): String =
    when (language) {
        AppLanguage.SYSTEM -> stringResource(R.string.language_system, systemLanguageName)
        AppLanguage.TURKISH -> stringResource(R.string.language_turkish)
        AppLanguage.ENGLISH -> stringResource(R.string.language_english)
    }

/** "Türkçe", "English": a language's name in that language, capitalized. */
private fun systemLanguageName(locale: Locale?): String {
    val target = locale ?: Locale.getDefault()
    return target.getDisplayLanguage(target).replaceFirstChar { it.titlecase(target) }
}
