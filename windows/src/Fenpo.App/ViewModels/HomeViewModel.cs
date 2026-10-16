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
    private readonly PhoneActions _actions;
    private readonly FileTransferService _transfers;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _clock;
    private readonly DispatcherQueueTimer _transfersRefresh;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeViewModel"/> class.
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="notifications">Notification store.</param>
    /// <param name="media">Phone media player.</param>
    /// <param name="icons">App icons.</param>
    /// <param name="localization">Strings.</param>
    /// <param name="actions">Ring the phone.</param>
    /// <param name="transfers">File transfers (recent transfers card).</param>
    public HomeViewModel(IPhoneStatusSource status, NotificationStore notifications, IMediaSource media, AppIconService icons, LocalizationService localization, PhoneActions actions, FileTransferService transfers)
    {
        _actions = actions;
        _transfers = transfers;

        // Progress arrives many times a second; the card follows twice a second.
        _transfersRefresh = _dispatcher.CreateTimer();
        _transfersRefresh.Interval = TimeSpan.FromMilliseconds(500);
        _transfersRefresh.IsRepeating = false;
        _transfersRefresh.Tick += (sender, e) => ApplyTransfers();
        _transfers.Changed += OnTransfersChanged;
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
        ApplyTransfers();
        Tick();
    }

    /// <summary>Gets the three newest notifications.</summary>
    public ObservableCollection<NotificationRowViewModel> RecentNotifications { get; } = [];

    /// <summary>Gets the three newest file transfers.</summary>
    public ObservableCollection<TransferRowViewModel> RecentTransfers { get; } = [];

    /// <summary>Gets or sets a value indicating whether there are no transfers yet.</summary>
    [ObservableProperty]
    public partial bool NoTransfers { get; set; } = true;

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

    /// <summary>Gets or sets a value indicating whether Fenpo on the phone can ring.</summary>
    [ObservableProperty]
    public partial bool CanRing { get; set; }

    /// <summary>Gets or sets the other paired phone's name, or empty when there is only one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtherPhone))]
    public partial string OtherPhoneText { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether a second phone is paired (the switch link shows).</summary>
    public bool HasOtherPhone => OtherPhoneText.Length > 0;

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
        _transfersRefresh.Stop();
        _transfers.Changed -= OnTransfersChanged;
        _status.Changed -= OnStatusChanged;
        _notifications.Changed -= OnNotificationsChanged;
        _media.Changed -= OnMediaChanged;
    }

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

    [RelayCommand]
    private Task PreviousAsync() => _media.SendAsync(MediaCommand.PreviousTrack);

    private void OnStatusChanged(object? sender, PhoneStatus status) => _dispatcher.TryEnqueue(() => ApplyStatus(status));

    private void OnNotificationsChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(ApplyNotifications);

    private void OnTransfersChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        if (!_transfersRefresh.IsRunning)
        {
            _transfersRefresh.Start();
        }
    });

    private void ApplyTransfers()
    {
        RecentTransfers.Clear();

        foreach (var transfer in _transfers.Transfers.Take(3))
        {
            RecentTransfers.Add(new TransferRowViewModel(transfer, _transfers, null, _localization));
        }

        NoTransfers = RecentTransfers.Count == 0;
    }

    private void OnMediaChanged(object? sender, NowPlaying nowPlaying) => _dispatcher.TryEnqueue(() => ApplyMedia(nowPlaying));

    private void ApplyStatus(PhoneStatus status)
    {
        IsPaired = status.State != PhoneConnectionState.NotPaired;
        DeviceName = PhoneStatusText.Name(status, _localization);
        StatusText = PhoneStatusText.State(status, _localization);
        BatteryText = PhoneStatusText.Battery(status, _localization);
        BatteryFill = (status.BatteryPercent ?? 0) * 0.17;
        BluetoothOn = status.BluetoothConnected;
        WifiOn = status.WifiConnected;
        WifiText = _localization.GetString(status.WifiConnected ? "Chip_WifiOn" : "Chip_WifiOff");
        UsbOn = status.UsbConnected;
        UsbText = _localization.GetString(status.UsbConnected ? "Chip_UsbOn" : "Chip_UsbOff");
        AppConnected = status.AppConnected;
        CanRing = _actions.CanRing;
        var other = _status.StatusOf(_status.Active == PhoneKind.Iphone ? PhoneKind.Android : PhoneKind.Iphone);
        OtherPhoneText = other.State == PhoneConnectionState.NotPaired ? string.Empty : _localization.Format("Phone_SwitchTo", PhoneStatusText.Name(other, _localization));
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
