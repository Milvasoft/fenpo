using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Fenpo.Core.Settings;
using Microsoft.Windows.AppLifecycle;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Settings page: appearance and language, features, proximity and focus, device.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] _languages = [AppSettings.SystemLanguage, "tr-TR", "en-US"];

    private readonly ISettingsStore _settings;
    private readonly LocalizationService _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">Settings store.</param>
    /// <param name="localization">Strings and the language restart state.</param>
    public SettingsViewModel(ISettingsStore settings, LocalizationService localization)
    {
        _settings = settings;
        _localization = localization;

        var current = settings.Current;
        ThemeIndex = (int)current.Theme;
        LanguageIndex = Math.Max(0, Array.IndexOf(_languages, current.Language));
        ShowNotifications = current.ShowNotifications;
        ShowIncomingCalls = current.ShowIncomingCalls;
        ShareClipboard = current.ShareClipboard;
        MediaControl = current.MediaControl;
        ProximityLock = current.ProximityLock;
        FocusSync = current.FocusSync;
        RestartPending = localization.RestartPending;
    }

    /// <summary>
    /// Gets the label of the "System language" option, with the language it resolves to.
    /// </summary>
    public string SystemLanguageLabel => _localization.Format("Language_System", LocalizationService.SystemLanguageName);

    /// <summary>
    /// Gets the restart banner text naming the language Fenpo switches to.
    /// </summary>
    public string RestartMessage => _localization.Format("LanguageRestart_Message", LanguageIndex switch
    {
        1 => "Türkçe",
        2 => "English",
        _ => LocalizationService.SystemLanguageName,
    });

    /// <summary>
    /// Gets or sets the selected theme: 0 System, 1 Light, 2 Dark.
    /// </summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    /// <summary>
    /// Gets or sets the selected language: 0 System, 1 Türkçe, 2 English.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestartMessage))]
    public partial int LanguageIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the restart banner is visible.
    /// </summary>
    [ObservableProperty]
    public partial bool RestartPending { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether iPhone notifications appear in Windows.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowNotifications { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether incoming calls appear in the island.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowIncomingCalls { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the clipboard is shared.
    /// </summary>
    [ObservableProperty]
    public partial bool ShareClipboard { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether media keys control the phone.
    /// </summary>
    [ObservableProperty]
    public partial bool MediaControl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the PC locks when the phone moves away.
    /// </summary>
    [ObservableProperty]
    public partial bool ProximityLock { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Fenpo goes quiet during a phone Focus.
    /// </summary>
    [ObservableProperty]
    public partial bool FocusSync { get; set; }

    partial void OnThemeIndexChanged(int value) => _settings.Update(s => s with { Theme = (AppTheme)value });

    partial void OnLanguageIndexChanged(int value)
    {
        _settings.Update(s => s with { Language = _languages[value] });
        RestartPending = _localization.RestartPending;
    }

    partial void OnShowNotificationsChanged(bool value) => _settings.Update(s => s with { ShowNotifications = value });

    partial void OnShowIncomingCallsChanged(bool value) => _settings.Update(s => s with { ShowIncomingCalls = value });

    partial void OnShareClipboardChanged(bool value) => _settings.Update(s => s with { ShareClipboard = value });

    partial void OnMediaControlChanged(bool value) => _settings.Update(s => s with { MediaControl = value });

    partial void OnProximityLockChanged(bool value) => _settings.Update(s => s with { ProximityLock = value });

    partial void OnFocusSyncChanged(bool value) => _settings.Update(s => s with { FocusSync = value });

    [RelayCommand]
    private static void Restart() => AppInstance.Restart(string.Empty);

    [RelayCommand]
    private void DismissRestart() => RestartPending = false;
}
