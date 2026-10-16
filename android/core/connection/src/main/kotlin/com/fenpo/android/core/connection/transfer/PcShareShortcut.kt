package com.fenpo.android.core.connection.transfer

import android.content.Context
import android.content.Intent
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import com.fenpo.android.core.designsystem.R as DesignR

/**
 * The paired PC as a direct share target (Sharing Shortcuts, docs/ANDROID.md §9): it shows up next
 * to contacts in the share sheet and sends straight to the PC. The category matches
 * `res/xml/shortcuts.xml` in the app.
 */
object PcShareShortcut {
    private const val ID = "pc"
    private const val CATEGORY = "com.fenpo.android.category.SHARE_TO_PC"

    /** Publishes or renames the shortcut for [pcName]. */
    fun publish(
        context: Context,
        pcName: String,
    ) {
        val launch = context.packageManager.getLaunchIntentForPackage(context.packageName) ?: Intent(Intent.ACTION_MAIN)
        val shortcut =
            ShortcutInfoCompat
                .Builder(context, ID)
                .setShortLabel(pcName)
                .setLongLived(true)
                .setIcon(IconCompat.createWithResource(context, DesignR.drawable.ic_fenpo_logo))
                .setCategories(setOf(CATEGORY))
                .setIntent(launch.setAction(Intent.ACTION_MAIN))
                .build()
        ShortcutManagerCompat.pushDynamicShortcut(context, shortcut)
    }

    /** Removes it, e.g. after the PC is forgotten. */
    fun remove(context: Context) {
        ShortcutManagerCompat.removeLongLivedShortcuts(context, listOf(ID))
    }
}
