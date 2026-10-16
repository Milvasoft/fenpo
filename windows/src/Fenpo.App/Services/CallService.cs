using System.Text.Json;
using Fenpo.Core.Calls;
using Fenpo.Core.Notifications;
using Fenpo.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Windows.Media.Control;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// Incoming calls: shows the island and rings the PC while the phone rings, sends Answer and Decline to the phone, pauses
/// what the PC is playing for the length of the call and keeps the recent calls list.
/// </summary>
public sealed class CallService(
    NotificationStore notifications,
    NotificationRouter router,
    ISettingsStore settings,
    LocalizationService localization,
    ILogger<CallService> logger) : IHostedService
{
    private readonly NotificationStore _notifications = notifications;
    private readonly NotificationRouter _router = router;
    private readonly ISettingsStore _settings = settings;
    private readonly LocalizationService _localization = localization;
    private readonly ILogger<CallService> _logger = logger;
    private readonly string _logPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "calls.json");
    private DispatcherQueue? _dispatcher;
    private IslandWindow? _island;
    private string? _ringingId;
    private GlobalSystemMediaTransportControlsSession? _pausedSession;
    private MediaPlayer? _ringer;

    /// <summary>
    /// Gets the recent calls.
    /// </summary>
    public CallLog Log { get; private set; } = new();

    /// <summary>
    /// Creates the island on the UI thread. Call once after the main window exists.
    /// </summary>
    /// <param name="dispatcher">UI thread dispatcher.</param>
    public void AttachUi(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _island = new IslandWindow();
        _island.Responded += (sender, answer) => _ = RespondAsync(answer);
    }

    /// <summary>
    /// Shows a sample call on the island, for the screenshot checks.
    /// </summary>
    public void ShowSample() => _island?.ShowCall("Fenpo", _localization.GetString("Island_CallOnPhone"), canAnswer: true);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Log = new CallLog(LoadLog());
        Log.Changed += (sender, e) => SaveLog();
        _notifications.Arrived += (sender, stored) => OnArrived(stored);
        _notifications.Dismissed += (sender, id) => OnDismissed(id);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnArrived(StoredNotification stored)
    {
        var notification = stored.Notification;
        var caller = string.IsNullOrEmpty(notification.Title) ? notification.Message : notification.Title;

        switch (notification.Category)
        {
            case NotificationCategory.IncomingCall:
                Log.Ringing(caller, notification.Date ?? notification.ReceivedAt);

                if (_settings.Current.ShowIncomingCalls)
                {
                    _ringingId = stored.Id;
                    var detail = string.IsNullOrEmpty(notification.Subtitle)
                        ? _localization.GetString("Island_CallOnPhone")
                        : _localization.Format("Island_CallOnPhoneWith", notification.Subtitle);
                    _dispatcher?.TryEnqueue(() => _island?.ShowCall(caller, detail, notification.PositiveActionLabel is not null));
                }

                _ = PausePcMediaAsync();

                if (_settings.Current.RingForCalls)
                {
                    _ringingId ??= stored.Id;
                    Ring();
                }

                break;
            case NotificationCategory.MissedCall:
                Log.Missed(caller, notification.Date ?? notification.ReceivedAt);
                break;
        }
    }

    private void OnDismissed(string id)
    {
        // The phone removes the call notification when the call is answered, declined or stops ringing.
        if (id != _ringingId)
        {
            return;
        }

        _ringingId = null;
        StopRinging();
        _dispatcher?.TryEnqueue(() => _island?.Hide());
        _ = ResumePcMediaAsync();
    }

    private async Task RespondAsync(bool answer)
    {
        if (_ringingId is not { } id)
        {
            return;
        }

        StopRinging();
        var accepted = await _router.PerformActionAsync(id, answer);
        _logger.LogInformation("Call {Action}: {Accepted}", answer ? "answer" : "decline", accepted);
    }

    // The sound picked for calls in Windows' sound settings, played until the phone stops ringing.
    private void Ring()
    {
        try
        {
            _ringer ??= new MediaPlayer { IsLoopingEnabled = true, AudioCategory = MediaPlayerAudioCategory.Alerts };
            _ringer.Source = MediaSource.CreateFromUri(new Uri(CallSoundPath()));
            _ringer.Play();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UriFormatException)
        {
            _logger.LogWarning(exception, "Ringing on the PC failed");
        }
    }

    private void StopRinging()
    {
        try
        {
            _ringer?.Pause();
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            _logger.LogWarning(exception, "Stopping the ring failed");
        }
    }

    private static string CallSoundPath()
    {
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        var chosen = Registry.GetValue(@"HKEY_CURRENT_USER\AppEvents\Schemes\Apps\.Default\Notification.Looping.Call\.Current", null, null) as string;
        chosen = string.IsNullOrEmpty(chosen) ? null : Environment.ExpandEnvironmentVariables(chosen);

        // A bare name is a file in the Media folder. "None" (empty) still rings: Fenpo's own setting turns the ring off.
        if (chosen is not null && !Path.IsPathRooted(chosen))
        {
            chosen = Path.Combine(media, chosen);
        }

        return chosen is not null && File.Exists(chosen) ? chosen : Path.Combine(media, "Ring01.wav");
    }

    private async Task PausePcMediaAsync()
    {
        if (!_settings.Current.PauseMediaDuringCalls)
        {
            return;
        }

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();

            // Fenpo's own session mirrors the phone, which pauses by itself during a call.
            if (session is null || session.SourceAppUserModelId.StartsWith("Milvasoft.Fenpo", StringComparison.Ordinal)
                || session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                return;
            }

            if (await session.TryPauseAsync())
            {
                _pausedSession = session;
            }
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Pausing PC media for the call failed");
        }
    }

    private async Task ResumePcMediaAsync()
    {
        if (Interlocked.Exchange(ref _pausedSession, null) is not { } session)
        {
            return;
        }

        try
        {
            await session.TryPlayAsync();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Resuming PC media after the call failed");
        }
    }

    private List<CallEntry> LoadLog()
    {
        try
        {
            return File.Exists(_logPath) ? JsonSerializer.Deserialize<List<CallEntry>>(File.ReadAllText(_logPath)) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _logger.LogWarning(exception, "Recent calls could not be read; starting empty");
            return [];
        }
    }

    private void SaveLog()
    {
        try
        {
            File.WriteAllText(_logPath, JsonSerializer.Serialize(Log.Entries));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Saving recent calls failed");
        }
    }
}
