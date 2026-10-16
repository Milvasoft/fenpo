package com.fenpo.android.core.data.notifications

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.core.stringSetPreferencesKey
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import org.json.JSONObject
import javax.inject.Inject
import javax.inject.Singleton

/**
 * An app that posted a notification on this phone, as listed in Settings > "Bildirim gönderen
 * uygulamalar".
 *
 * @property replyable One of its notifications had a reply box, so it can be answered from the PC.
 */
data class NotificationApp(
    val appId: String,
    val name: String,
    val replyable: Boolean,
    val enabled: Boolean,
)

/**
 * Which apps' notifications go to the PC.
 *
 * @property mirrorNewApps Apps seen for the first time are on (design: "Yeni uygulamalar da gönderilsin").
 * @property apps Apps seen so far, by name.
 */
data class NotificationAppSettings(
    val mirrorNewApps: Boolean = true,
    val apps: List<NotificationApp> = emptyList(),
    private val included: Set<String> = emptySet(),
    private val excluded: Set<String> = emptySet(),
) {
    /** Whether [appId]'s notifications go to the PC. */
    fun isEnabled(appId: String): Boolean = appId in included || (appId !in excluded && mirrorNewApps)
}

/** Stores [NotificationAppSettings]. */
interface NotificationAppsRepository {
    val settings: Flow<NotificationAppSettings>

    suspend fun setMirrorNewApps(enabled: Boolean)

    suspend fun setAppEnabled(
        appId: String,
        enabled: Boolean,
    )

    /** Remembers an app that posted a notification; [replyable] sticks once true. */
    suspend fun recordSeen(
        appId: String,
        name: String,
        replyable: Boolean,
    )
}

@Singleton
internal class DataStoreNotificationAppsRepository
    @Inject
    constructor(
        private val dataStore: DataStore<Preferences>,
    ) : NotificationAppsRepository {
        override val settings: Flow<NotificationAppSettings> =
            dataStore.data.map { prefs ->
                val included = prefs[INCLUDED].orEmpty()
                val excluded = prefs[EXCLUDED].orEmpty()
                val mirrorNew = prefs[MIRROR_NEW] ?: true
                val base = NotificationAppSettings(mirrorNew, emptyList(), included, excluded)
                val seen = decode(prefs[SEEN])
                base.copy(
                    apps =
                        seen
                            .map { (id, entry) -> NotificationApp(id, entry.first, entry.second, base.isEnabled(id)) }
                            .sortedBy { it.name.lowercase() },
                )
            }

        override suspend fun setMirrorNewApps(enabled: Boolean) {
            dataStore.edit { it[MIRROR_NEW] = enabled }
        }

        override suspend fun setAppEnabled(
            appId: String,
            enabled: Boolean,
        ) {
            dataStore.edit {
                val included = it[INCLUDED].orEmpty()
                val excluded = it[EXCLUDED].orEmpty()
                it[INCLUDED] = if (enabled) included + appId else included - appId
                it[EXCLUDED] = if (enabled) excluded - appId else excluded + appId
            }
        }

        override suspend fun recordSeen(
            appId: String,
            name: String,
            replyable: Boolean,
        ) {
            val known = decode(dataStore.data.first()[SEEN])[appId]
            if (known != null && known.first == name && (known.second || !replyable)) return
            dataStore.edit {
                val seen = decode(it[SEEN]).toMutableMap()
                seen[appId] = name to (replyable || seen[appId]?.second == true)
                it[SEEN] = encode(seen)
            }
        }

        private companion object {
            val INCLUDED = stringSetPreferencesKey("notification_apps_included")
            val EXCLUDED = stringSetPreferencesKey("notification_apps_excluded")
            val MIRROR_NEW = booleanPreferencesKey("notification_apps_mirror_new")
            val SEEN = stringPreferencesKey("notification_apps_seen")

            fun encode(seen: Map<String, Pair<String, Boolean>>): String =
                JSONObject()
                    .apply {
                        seen.forEach { (id, entry) ->
                            put(id, JSONObject().put("name", entry.first).put("reply", entry.second))
                        }
                    }.toString()

            fun decode(text: String?): Map<String, Pair<String, Boolean>> {
                val json = text?.let { runCatching { JSONObject(it) }.getOrNull() } ?: return emptyMap()
                return json.keys().asSequence().associateWith { id ->
                    val o = json.optJSONObject(id)
                    (o?.optString("name").orEmpty().ifEmpty { id }) to (o?.optBoolean("reply") == true)
                }
            }
        }
    }
