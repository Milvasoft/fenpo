package com.fenpo.android.feature.pairing

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.core.app.ActivityCompat
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.fenpo.android.core.data.permissions.AppPermission
import com.fenpo.android.core.designsystem.component.FenpoListRow
import com.fenpo.android.core.designsystem.component.FenpoSubTopBar
import com.fenpo.android.core.designsystem.component.LeadingIcon
import com.fenpo.android.core.designsystem.component.NoteBox
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.core.designsystem.theme.FenpoTheme

/** Permissions screen with its view model; opens the right system dialog or Settings page per row. */
@Composable
fun PermissionsRoute(
    onBack: () -> Unit,
    modifier: Modifier = Modifier,
    viewModel: PermissionsViewModel = hiltViewModel(),
) {
    val items by viewModel.items.collectAsStateWithLifecycle()
    val context = LocalContext.current
    LifecycleResumeEffect(Unit) {
        viewModel.refresh()
        onPauseOrDispose {}
    }
    val launcher =
        rememberLauncherForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { results ->
            viewModel.refresh()
            // Denied for good: Android no longer shows its dialog, so open the app's settings page instead.
            val activity = context.findActivity()
            val blocked =
                results.any { (permission, granted) ->
                    !granted && activity != null &&
                        !ActivityCompat.shouldShowRequestPermissionRationale(activity, permission)
                }
            if (blocked) context.openSettings(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, withPackage = true)
        }
    PermissionsScreen(
        items = items,
        onBack = onBack,
        onFix = { permission ->
            when (permission) {
                AppPermission.NOTIFICATION_ACCESS -> {
                    context.openSettings(
                        Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS,
                    )
                }

                AppPermission.BATTERY -> {
                    context.openSettings(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
                }

                else -> {
                    launcher.launch(permission.runtimePermissions.toTypedArray())
                }
            }
        },
        modifier = modifier,
    )
}

/**
 * Permissions (design: "İzinler"): every permission Fenpo needs on one screen, with the button
 * that fixes a missing one next to it.
 */
@Composable
fun PermissionsScreen(
    items: List<PermissionItem>,
    onBack: () -> Unit,
    onFix: (AppPermission) -> Unit,
    modifier: Modifier = Modifier,
) {
    val granted = items.count { it.granted }
    val missing = items.size - granted
    Column(
        modifier =
            modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState()),
    ) {
        FenpoSubTopBar(title = stringResource(R.string.permissions_title), onBack = onBack)
        Column(modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 2.dp, bottom = 6.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(
                    text = stringResource(R.string.permissions_ready, granted, items.size),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Text(
                    text =
                        if (missing == 0) {
                            stringResource(R.string.permissions_all_granted)
                        } else {
                            pluralStringResource(R.plurals.permissions_missing, missing, missing)
                        },
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            LinearProgressIndicator(
                progress = { if (items.isEmpty()) 0f else granted / items.size.toFloat() },
                modifier = Modifier.fillMaxWidth(),
                drawStopIndicator = {},
            )
        }
        items.forEach { item ->
            val text = item.permission.text()
            FenpoListRow(
                title = stringResource(text.title),
                supporting = stringResource(text.summary),
                leading = { LeadingIcon(text.icon) },
                trailing = {
                    if (item.granted) {
                        Granted()
                    } else {
                        Button(onClick = { onFix(item.permission) }, modifier = Modifier.height(32.dp)) {
                            Text(stringResource(text.action), style = MaterialTheme.typography.labelLarge)
                        }
                    }
                },
            )
        }
        if (items.any { it.permission == AppPermission.NOTIFICATION_ACCESS && !it.granted }) {
            NoteBox(
                text = stringResource(R.string.permissions_restricted_hint),
                icon = FenpoIcons.Info,
                modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 16.dp),
            )
        }
    }
}

@Composable
private fun Granted() {
    val color = FenpoTheme.extendedColors.success
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        Icon(
            painterResource(FenpoIcons.Check),
            contentDescription = null,
            modifier = Modifier.size(18.dp),
            tint = color,
        )
        Text(stringResource(R.string.permission_granted), style = MaterialTheme.typography.labelLarge, color = color)
    }
}

private data class PermissionText(
    @param:StringRes val title: Int,
    @param:StringRes val summary: Int,
    @param:DrawableRes val icon: Int,
    @param:StringRes val action: Int,
)

private fun AppPermission.text(): PermissionText =
    when (this) {
        AppPermission.NEARBY_DEVICES -> {
            PermissionText(
                R.string.permission_nearby,
                R.string.permission_nearby_summary,
                FenpoIcons.Bluetooth,
                R.string.permission_allow,
            )
        }

        AppPermission.POST_NOTIFICATIONS -> {
            PermissionText(
                R.string.permission_notifications,
                R.string.permission_notifications_summary,
                FenpoIcons.Arrows,
                R.string.permission_allow,
            )
        }

        AppPermission.NOTIFICATION_ACCESS -> {
            PermissionText(
                R.string.permission_notification_access,
                R.string.permission_notification_access_summary,
                FenpoIcons.Bell,
                R.string.permission_turn_on,
            )
        }

        AppPermission.BATTERY -> {
            PermissionText(
                R.string.permission_battery,
                R.string.permission_battery_summary,
                FenpoIcons.Battery,
                R.string.permission_remove,
            )
        }

        AppPermission.SMS -> {
            PermissionText(
                R.string.permission_sms,
                R.string.permission_sms_summary,
                FenpoIcons.Message,
                R.string.permission_allow,
            )
        }

        AppPermission.CAMERA -> {
            PermissionText(
                R.string.permission_camera,
                R.string.permission_camera_summary,
                FenpoIcons.Camera,
                R.string.permission_allow,
            )
        }
    }

private fun Context.openSettings(
    action: String,
    withPackage: Boolean = false,
) {
    val intent = Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    if (withPackage) intent.data = Uri.fromParts("package", packageName, null)
    startActivity(intent)
}

private tailrec fun Context.findActivity(): Activity? =
    when (this) {
        is Activity -> this
        is ContextWrapper -> baseContext.findActivity()
        else -> null
    }
