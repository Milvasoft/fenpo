using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Fenpo.Core.Devices;
using Fenpo.Core.Media;
using Fenpo.Core.Settings;
using Microsoft.UI.Dispatching;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Tray panel: phone summary, quick toggles, now playing. Lives as long as the app.
/// </summary>
public sealed partial class TrayViewModel : ObservableObject
{
    private readonly IPhoneStatusSource _status;
    private readonly IMediaSource _media;
    private readonly ISettingsStore _settings;
    private readonly LocalizationService _localization;
    private readonly PhoneActions _actions;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _applyingSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayViewModel"/> class.
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="media">Phone media player.</param>
    /// <param name="settings">Settings store (quick toggles).</param>
    /// <param name="localization">Strings.</param>
    /// <param name="actions">Ring the phone.</param>
    public TrayViewModel(IPhoneStatusSource status, IMediaSource media, ISettingsStore settings, LocalizationService localization, PhoneActions actions)
    {
        _actions = actions;
        _status = status;
        _media = media;
        _settings = settings;
        _localization = localization;

        _status.Changed += (sender, value) => _dispatcher.TryEnqueue(() => ApplyStatus(value));
        _media.Changed += (sender, value) => _dispatcher.TryEnqueue(() => ApplyMedia(value));
        _settings.Changed += (sender, value) => _dispatcher.TryEnqueue(() => ApplySettings(value));

        ApplyStatus(_status.Current);
        ApplyMedia(_media.Current);
        ApplySettings(_settings.Current);
    }

    /// <summary>Gets or sets the tray icon tooltip.</summary>
    [ObservableProperty]
    public partial string ToolTip { get; set; } = "Fenpo";

    /// <summary>Gets or sets the phone name.</summary>
    [ObservableProperty]
    public partial string DeviceName { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-line summary ("%78, Bluetooth ve Wi‑Fi").</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>Gets or sets the connection caption for the footer.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the iPhone app is connected (app-only toggles).</summary>
    [ObservableProperty]
    public partial bool AppConnected { get; set; }

    /// <summary>Gets or sets a value indicating whether phone notifications are shown.</summary>
    [ObservableProperty]
    public partial bool ShowNotifications { get; set; }

    /// <summary>Gets or sets a value indicating whether the clipboard is shared.</summary>
    [ObservableProperty]
    public partial bool ShareClipboard { get; set; }

    /// <summary>Gets or sets a value indicating whether proximity lock is on.</summary>
    [ObservableProperty]
    public partial bool ProximityLock { get; set; }

    /// <summary>Gets or sets a value indicating whether Focus is synced.</summary>
    [ObservableProperty]
    public partial bool FocusSync { get; set; }

    /// <summary>Gets or sets a value indicating whether something is playing.</summary>
    [ObservableProperty]
    public partial bool HasTrack { get; set; }

    /// <summary>Gets or sets the track title.</summary>
    [ObservableProperty]
    public partial string TrackTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the artist.</summary>
    [ObservableProperty]
    public partial string TrackArtist { get; set; } = string.Empty;

    /// <summary>Gets or sets the play/pause glyph.</summary>
    [ObservableProperty]
    public partial string PlayPauseGlyph { get; set; } = "";

    /// <summary>Gets or sets a value indicating whether Fenpo on the phone can ring.</summary>
    [ObservableProperty]
    public partial bool CanRing { get; set; }

    /// <summary>Gets or sets the other paired phone's name, or empty when there is only one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtherPhone))]
    public partial string OtherPhoneText { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether a second phone is paired (the switch link shows).</summary>
    public bool HasOtherPhone => OtherPhoneText.Length > 0;

    /// <summary>Gets or sets a value indicating whether the ring just went out (the button says so for a moment).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotRinging))]
    public partial bool IsRinging { get; set; }

    /// <summary>Gets a value indicating whether the button shows its usual text.</summary>
    public bool NotRinging => !IsRinging;

    // The command stays busy (button disabled) while the button says the phone is ringing, so it is not pressed twice.
    [RelayCommand]
    private async Task RingAsync()
    {
        if (await _actions.RingAsync())
        {
            IsRinging = true;
            await Task.Delay(TimeSpan.FromSeconds(5));
            IsRinging = false;
        }
    }

    [RelayCommand]
    private void UseOtherPhone() => _status.Use(_status.Active == PhoneKind.Iphone ? PhoneKind.Android : PhoneKind.Iphone);

    [RelayCommand]
    private Task PlayPauseAsync() => _media.SendAsync(MediaCommand.TogglePlayPause);

    [RelayCommand]
    private Task NextAsync() => _media.SendAsync(MediaCommand.NextTrack);

    partial void OnShowNotificationsChanged(bool value) => Save(s => s with { ShowNotifications = value });

    partial void OnShareClipboardChanged(bool value) => Save(s => s with { ShareClipboard = value });

    partial void OnProximityLockChanged(bool value) => Save(s => s with { ProximityLock = value });

    partial void OnFocusSyncChanged(bool value) => Save(s => s with { FocusSync = value });

    private void Save(Func<AppSettings, AppSettings> change)
    {
        if (!_applyingSettings)
        {
            _settings.Update(change);
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        _applyingSettings = true;
        ShowNotifications = settings.ShowNotifications;
        ShareClipboard = settings.ShareClipboard;
        ProximityLock = settings.ProximityLock;
        FocusSync = settings.FocusSync;
        _applyingSettings = false;
    }

    private void ApplyStatus(PhoneStatus status)
    {
        DeviceName = PhoneStatusText.Name(status, _localization);
        Summary = PhoneStatusText.Summary(status, _localization);
        StatusText = PhoneStatusText.State(status, _localization);
        AppConnected = status.AppConnected;
        CanRing = _actions.CanRing;
        var other = _status.StatusOf(_status.Active == PhoneKind.Iphone ? PhoneKind.Android : PhoneKind.Iphone);
        OtherPhoneText = other.State == PhoneConnectionState.NotPaired ? string.Empty : _localization.Format("Phone_SwitchTo", PhoneStatusText.Name(other, _localization));
        ToolTip = status.State == PhoneConnectionState.NotPaired ? "Fenpo" : $"Fenpo\n{DeviceName}: {Summary}";
    }

    private void ApplyMedia(NowPlaying nowPlaying)
    {
        HasTrack = nowPlaying.HasTrack;
        TrackTitle = nowPlaying.Title;
        TrackArtist = nowPlaying.Artist;
        PlayPauseGlyph = nowPlaying.State == PlaybackState.Playing ? "" : "";
    }
}
