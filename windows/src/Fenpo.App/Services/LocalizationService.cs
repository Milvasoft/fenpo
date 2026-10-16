using System.Globalization;
using Fenpo.Core.Settings;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Fenpo.App.Services;

/// <summary>
/// Language setting (System, Türkçe, English). Resources load at startup, so a change takes effect after a restart;
/// the settings page offers one.
/// </summary>
public sealed class LocalizationService(ISettingsStore settings)
{
    private static string _startupLanguage = AppSettings.SystemLanguage;

    private readonly ISettingsStore _settings = settings;
    private readonly ResourceLoader _loader = new();

    /// <summary>
    /// Gets a value indicating whether the saved language differs from the one this process started with.
    /// </summary>
    public bool RestartPending => _settings.Current.Language != _startupLanguage;

    /// <summary>
    /// Gets the language Windows would pick for the "System language" option, as a display name in that language.
    /// </summary>
    public static string SystemLanguageName
    {
        get
        {
            var tag = Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? "en-US";
            var culture = new CultureInfo(tag);
            var name = culture.TwoLetterISOLanguageName == "tr" ? "Türkçe" : "English";
            return name;
        }
    }

    /// <summary>
    /// Sets the primary language override before any resource is loaded.
    /// </summary>
    /// <param name="language">The saved language setting.</param>
    public static void ApplyAtStartup(string language)
    {
        _startupLanguage = language;

        // The packaged override persists between launches. The WinRT setter accepts an empty string to clear it
        // (the Windows App SDK one does not), and MRT Core reads the same value in a packaged app.
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language == AppSettings.SystemLanguage ? string.Empty : language;

        // Dates, numbers and month names follow the language the resources resolved to, not the Windows display language.
        var resolved = Windows.Globalization.ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";
        var culture = new CultureInfo(resolved);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>
    /// Gets a localized string from Strings/&lt;language&gt;/Resources.resw.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The string, or the key when it is missing.</returns>
    public string GetString(string key)
    {
        var value = _loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    /// <summary>
    /// Gets a localized format string and fills it in.
    /// </summary>
    /// <param name="key">Resource key of a composite format string.</param>
    /// <param name="arguments">Format arguments.</param>
    /// <returns>The formatted string.</returns>
    public string Format(string key, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, GetString(key), arguments);
}
