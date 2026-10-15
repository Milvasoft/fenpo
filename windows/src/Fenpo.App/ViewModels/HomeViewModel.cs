using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Fenpo.Core.Devices;
using Fenpo.Core.Media;
using Fenpo.Core.Notifications;
using Microsoft.UI.Dispatching;

namespace Fenpo.App.ViewModels;

/// <summary>
/// "My iPhone" page: phone status, recent notifications, now playing.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly IPhoneStatusSource _status;
    private readonly NotificationStore _notifications;
    private readonly IMediaSource _media;
    private readonly AppIconService _icons;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeViewModel"/> class.
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="notifications">Notification store.</param>
    /// <param name="media">Phone media player.</param>
    /// <param name="icons">App icons.</param>
    /// <param name="localization">Strings.</param>
    public HomeViewModel(IPhoneStatusSource status, NotificationStore notifications, IMediaSource media, AppIconService icons, LocalizationService localization)
    {
        _status = status;
        _notifications = notifications;
        _media = media;
        _icons = icons;
        _localization = localization;

        _status.Changed += OnStatusChanged;
        _notifications.Changed += OnNotificationsChanged;
        _media.Changed += OnMediaChanged;

        _clock = _dispatcher.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => Tick();
        _clock.Start();

        ApplyStatus(_status.Current);
        ApplyNotifications();
        ApplyMedia(_media.Current);
        Tick();
    }

    /// <summary>Gets the three newest notifications.</summary>
    public ObservableCollection<NotificationRowViewModel> RecentNotifications { get; } = [];

    /// <summary>Gets or sets a value indicating whether a phone is paired.</summary>
    [ObservableProperty]
    public partial bool IsPaired { get; set; }

    /// <summary>Gets or sets the connection caption ("Bağlı").</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Gets or sets the phone name.</summary>
    [ObservableProperty]
    public partial string DeviceName { get; set; } = string.Empty;

    /// <summary>Gets or sets the battery text ("%78").</summary>
    [ObservableProperty]
    public partial string BatteryText { get; set; } = "–";

    /// <summary>Gets or sets the battery fill width for the glyph (0..17).</summary>
    [ObservableProperty]
    public partial double BatteryFill { get; set; }

    /// <summary>Gets or sets a value indicating whether Bluetooth is connected.</summary>
    [ObservableProperty]
    public partial bool BluetoothOn { get; set; }

    /// <summary>Gets or sets the Wi‑Fi chip text.</summary>
    [ObservableProperty]
    public partial string WifiText { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the data channel is up.</summary>
    [ObservableProperty]
    public partial bool WifiOn { get; set; }

    /// <summary>Gets or sets the USB chip text.</summary>
    [ObservableProperty]
    public partial string UsbText { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether USB is connected.</summary>
    [ObservableProperty]
    public partial bool UsbOn { get; set; }

    /// <summary>Gets or sets a value indicating whether the iPhone app is connected (app-only actions).</summary>
    [ObservableProperty]
    public partial bool AppConnected { get; set; }

    /// <summary>Gets or sets the clock on the phone illustration.</summary>
    [ObservableProperty]
    public partial string ClockText { get; set; } = string.Empty;

    /// <summary>Gets or sets the date on the phone illustration.</summary>
    [ObservableProperty]
    public partial string DateText { get; set; } = string.Empty;

    /// <summary>Gets or sets the newest notification's sender, shown on the phone illustration.</summary>
    [ObservableProperty]
    public partial string MiniTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the newest notification's text, shown on the phone illustration.</summary>
    [ObservableProperty]
    public partial string MiniMessage { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether there are notifications.</summary>
    [ObservableProperty]
    public partial bool HasNotifications { get; set; }

    /// <summary>Gets or sets a value indicating whether something is playing.</summary>
    [ObservableProperty]
    public partial bool HasTrack { get; set; }

    /// <summary>Gets or sets the track title.</summary>
    [ObservableProperty]
    public partial string TrackTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the artist.</summary>
    [ObservableProperty]
    public partial string TrackArtist { get; set; } = string.Empty;

    /// <summary>Gets or sets the elapsed time text.</summary>
    [ObservableProperty]
    public partial string PositionText { get; set; } = string.Empty;

    /// <summary>Gets or sets the duration text.</summary>
    [ObservableProperty]
    public partial string DurationText { get; set; } = string.Empty;

    /// <summary>Gets or sets the progress from 0 to 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>Gets or sets a value indicating whether the phone is playing.</summary>
    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    /// <summary>Gets or sets the play/pause glyph.</summary>
    [ObservableProperty]
    public partial string PlayPauseGlyph { get; set; } = "";

    /// <inheritdoc/>
    public void Dispose()
    {
        _clock.Stop();
        _status.Changed -= OnStatusChanged;
        _notifications.Changed -= OnNotificationsChanged;
        _media.Changed -= OnMediaChanged;
    }

    [RelayCommand]
    private Task PlayPauseAsync() => _media.SendAsync(MediaCommand.TogglePlayPause);

    [RelayCommand]
    private Task NextAsync() => _media.SendAsync(MediaCommand.NextTrack);

    [RelayCommand]
    private Task PreviousAsync() => _media.SendAsync(MediaCommand.PreviousTrack);

    private void OnStatusChanged(object? sender, PhoneStatus status) => _dispatcher.TryEnqueue(() => ApplyStatus(status));

    private void OnNotificationsChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(ApplyNotifications);

    private void OnMediaChanged(object? sender, NowPlaying nowPlaying) => _dispatcher.TryEnqueue(() => ApplyMedia(nowPlaying));

    private void ApplyStatus(PhoneStatus status)
    {
        IsPaired = status.State != PhoneConnectionState.NotPaired;
        DeviceName = string.IsNullOrEmpty(status.Name) ? _localization.GetString("Home_NoPhone") : status.Name;
        StatusText = _localization.GetString(status.State switch
        {
            PhoneConnectionState.Connected => "Status_Connected",
            PhoneConnectionState.Connecting => "Status_Connecting",
            PhoneConnectionState.BrokenBond => "Status_BrokenBond",
            PhoneConnectionState.Disconnected => "Status_Disconnected",
            _ => "Status_NotPaired",
        });
        BatteryText = status.BatteryPercent is { } battery ? _localization.Format("Battery_Percent", battery) : "–";
        BatteryFill = (status.BatteryPercent ?? 0) * 0.17;
        BluetoothOn = status.BluetoothConnected;
        WifiOn = status.WifiConnected;
        WifiText = _localization.GetString(status.WifiConnected ? "Chip_WifiOn" : "Chip_WifiOff");
        UsbOn = status.UsbConnected;
        UsbText = _localization.GetString(status.UsbConnected ? "Chip_UsbOn" : "Chip_UsbOff");
        AppConnected = status.AppConnected;
    }

    private void ApplyNotifications()
    {
        var items = _notifications.Items;
        RecentNotifications.Clear();

        foreach (var item in items.Take(3))
        {
            RecentNotifications.Add(new NotificationRowViewModel(item, _icons.GetCachedIconPath(item.Notification.AppId), NotificationRowViewModel.RelativeTime(item.Notification.Date ?? item.Notification.ReceivedAt, _localization)));
        }

        HasNotifications = RecentNotifications.Count > 0;
        var newest = items.FirstOrDefault()?.Notification;
        MiniTitle = newest?.Title ?? string.Empty;
        MiniMessage = newest?.Message ?? string.Empty;
    }

    private void ApplyMedia(NowPlaying nowPlaying)
    {
        HasTrack = nowPlaying.HasTrack;
        TrackTitle = nowPlaying.Title;
        TrackArtist = nowPlaying.Artist;
        IsPlaying = nowPlaying.State == PlaybackState.Playing;
        PlayPauseGlyph = IsPlaying ? "" : "";
        UpdatePosition(nowPlaying);
    }

    private void Tick()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm", CultureInfo.CurrentUICulture);
        DateText = now.ToString("d MMMM dddd", CultureInfo.CurrentUICulture);
        UpdatePosition(_media.Current);
    }

    private void UpdatePosition(NowPlaying nowPlaying)
    {
        if (!nowPlaying.HasTrack)
        {
            return;
        }

        var position = nowPlaying.PositionAt(DateTimeOffset.Now);
        PositionText = Format(position);
        DurationText = nowPlaying.Duration is { } duration ? Format(duration) : string.Empty;
        Progress = nowPlaying.Duration is { TotalSeconds: > 0 } total ? Math.Clamp(position / total * 100, 0, 100) : 0;
    }

    private static string Format(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
