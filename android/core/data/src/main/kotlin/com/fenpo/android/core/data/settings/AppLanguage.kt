package com.fenpo.android.core.data.settings

import androidx.appcompat.app.AppCompatDelegate
import androidx.core.os.LocaleListCompat
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Language choice in Settings. Stored by the system's per-app language setting (Android 13+), so
 * Settings > Apps > Fenpo > Language and this choice are always the same.
 */
enum class AppLanguage(
    internal val tag: String?,
) {
    SYSTEM(null),
    TURKISH("tr"),
    ENGLISH("en"),
}

/** Reads and applies the app language. Applying it recreates visible activities; no restart needed. */
interface LanguageController {
    /** The language currently chosen for the app. */
    fun current(): AppLanguage

    /** Applies [language] to the whole app. */
    fun apply(language: AppLanguage)
}

/** [LanguageController] backed by AppCompat's per-app locale API. */
@Singleton
internal class AppCompatLanguageController
    @Inject
    constructor() : LanguageController {
        override fun current(): AppLanguage {
            val locales = AppCompatDelegate.getApplicationLocales()
            if (locales.isEmpty) return AppLanguage.SYSTEM
            val language = locales[0]?.language
            return AppLanguage.entries.firstOrNull { it.tag != null && it.tag == language } ?: AppLanguage.SYSTEM
        }

        override fun apply(language: AppLanguage) {
            val locales =
                language.tag?.let { LocaleListCompat.forLanguageTags(it) } ?: LocaleListCompat.getEmptyLocaleList()
            AppCompatDelegate.setApplicationLocales(locales)
        }
    }
