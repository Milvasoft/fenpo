package com.fenpo.android.navigation

import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.lifecycle.viewmodel.navigation3.rememberViewModelStoreNavEntryDecorator
import androidx.navigation3.runtime.NavBackStack
import androidx.navigation3.runtime.NavKey
import androidx.navigation3.runtime.entryProvider
import androidx.navigation3.runtime.rememberNavBackStack
import androidx.navigation3.runtime.rememberSaveableStateHolderNavEntryDecorator
import androidx.navigation3.ui.NavDisplay
import com.fenpo.android.R
import com.fenpo.android.core.designsystem.icon.FenpoIcons
import com.fenpo.android.feature.camera.CameraActivity
import com.fenpo.android.feature.home.HomeKey
import com.fenpo.android.feature.home.HomeRoute
import com.fenpo.android.feature.home.PcKey
import com.fenpo.android.feature.home.PcRoute
import com.fenpo.android.feature.pairing.PairingStepsKey
import com.fenpo.android.feature.pairing.PairingStepsRoute
import com.fenpo.android.feature.pairing.PermissionsKey
import com.fenpo.android.feature.pairing.PermissionsRoute
import com.fenpo.android.feature.pairing.ScannerKey
import com.fenpo.android.feature.pairing.ScannerRoute
import com.fenpo.android.feature.remote.PresenterKey
import com.fenpo.android.feature.remote.PresenterRoute
import com.fenpo.android.feature.remote.TouchpadKey
import com.fenpo.android.feature.remote.TouchpadRoute
import com.fenpo.android.feature.settings.NotificationAppsKey
import com.fenpo.android.feature.settings.NotificationAppsRoute
import com.fenpo.android.feature.settings.SettingsKey
import com.fenpo.android.feature.settings.SettingsRoute
import com.fenpo.android.feature.transfers.TransfersKey
import com.fenpo.android.feature.transfers.TransfersRoute

/** A tab of the bottom navigation bar (design: Fenpo, Aktarımlar, PC, Ayarlar). */
private data class TopLevelDestination(
    val key: NavKey,
    @param:DrawableRes val icon: Int,
    @param:StringRes val label: Int,
)

private val topLevelDestinations =
    listOf(
        TopLevelDestination(HomeKey, FenpoIcons.House, R.string.nav_home),
        TopLevelDestination(TransfersKey, FenpoIcons.Arrows, R.string.nav_transfers),
        TopLevelDestination(PcKey, FenpoIcons.Pc, R.string.nav_pc),
        TopLevelDestination(SettingsKey, FenpoIcons.Gear, R.string.nav_settings),
    )

/**
 * App shell: four tabs with a Material 3 navigation bar and full-screen pushed screens (scanner,
 * permissions). Home is always at the bottom of the back stack, so Back from any tab returns to it.
 */
@Composable
fun FenpoApp(modifier: Modifier = Modifier) {
    val backStack = rememberNavBackStack(HomeKey)
    val context = LocalContext.current
    val current = backStack.lastOrNull()
    val showNavigationBar = topLevelDestinations.any { it.key == current }
    val pop: () -> Unit = { if (backStack.size > 1) backStack.removeAt(backStack.lastIndex) }

    Scaffold(
        modifier = modifier,
        containerColor = MaterialTheme.colorScheme.surface,
        contentWindowInsets = WindowInsets(0),
        bottomBar = {
            if (showNavigationBar) {
                FenpoNavigationBar(selected = current, onSelect = { backStack.selectTab(it) })
            }
        },
    ) { padding ->
        NavDisplay(
            backStack = backStack,
            onBack = pop,
            modifier = Modifier.padding(padding),
            entryDecorators =
                listOf(
                    rememberSaveableStateHolderNavEntryDecorator(),
                    rememberViewModelStoreNavEntryDecorator(),
                ),
            entryProvider =
                entryProvider {
                    entry<HomeKey> {
                        Inset {
                            HomeRoute(
                                onPair = { backStack.add(ScannerKey) },
                                onOpenNotificationSettings = { backStack.add(NotificationAppsKey) },
                                onOpenCamera = { context.startActivity(CameraActivity.intent(context)) },
                            )
                        }
                    }
                    entry<TransfersKey> { Inset { TransfersRoute() } }
                    entry<PcKey> {
                        Inset {
                            PcRoute(
                                onPair = { backStack.add(ScannerKey) },
                                onOpenTouchpad = { backStack.add(TouchpadKey) },
                                onOpenPresenter = { backStack.add(PresenterKey) },
                            )
                        }
                    }
                    entry<TouchpadKey> { Inset(bottom = true) { TouchpadRoute(onBack = pop) } }
                    entry<PresenterKey> { Inset(bottom = true) { PresenterRoute(onBack = pop) } }
                    entry<SettingsKey> {
                        Inset {
                            SettingsRoute(
                                onOpenPermissions = { backStack.add(PermissionsKey) },
                                onOpenNotificationApps = { backStack.add(NotificationAppsKey) },
                            )
                        }
                    }
                    entry<NotificationAppsKey> { Inset(bottom = true) { NotificationAppsRoute(onBack = pop) } }
                    entry<ScannerKey> {
                        ScannerRoute(
                            onBack = pop,
                            onCode = { code -> backStack.replaceTop(PairingStepsKey(code)) },
                        )
                    }
                    entry<PairingStepsKey> { key ->
                        Inset(bottom = true) {
                            PairingStepsRoute(
                                code = key.code,
                                onBack = pop,
                                onRescan = { backStack.replaceTop(ScannerKey) },
                                onOpenPermissions = { backStack.replaceTop(PermissionsKey) },
                                onDone = { backStack.selectTab(HomeKey) },
                            )
                        }
                    }
                    entry<PermissionsKey> { Inset(bottom = true) { PermissionsRoute(onBack = pop) } }
                },
        )
    }
}

@Composable
private fun FenpoNavigationBar(
    selected: NavKey?,
    onSelect: (NavKey) -> Unit,
) {
    NavigationBar(containerColor = MaterialTheme.colorScheme.surfaceContainer) {
        topLevelDestinations.forEach { destination ->
            NavigationBarItem(
                selected = destination.key == selected,
                onClick = { onSelect(destination.key) },
                icon = { Icon(painterResource(destination.icon), contentDescription = null) },
                label = { Text(stringResource(destination.label)) },
            )
        }
    }
}

/** Keeps a screen below the status bar (and above the gesture bar when there is no navigation bar). */
@Composable
private fun Inset(
    bottom: Boolean = false,
    content: @Composable () -> Unit,
) {
    val base = Modifier.fillMaxSize().statusBarsPadding()
    Box(if (bottom) base.navigationBarsPadding() else base) { content() }
}

/** Switches tabs: Home stays at the root, the chosen tab goes on top of it. */
private fun NavBackStack<NavKey>.selectTab(key: NavKey) {
    clear()
    add(HomeKey)
    if (key != HomeKey) add(key)
}

/** Replaces the top screen, so Back skips it (scanner → steps → permissions). */
private fun NavBackStack<NavKey>.replaceTop(key: NavKey) {
    if (size > 1) removeAt(lastIndex)
    add(key)
}
