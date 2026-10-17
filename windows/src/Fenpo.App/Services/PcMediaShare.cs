using Fenpo.App.Helpers;
using Fenpo.Protocol.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;

namespace Fenpo.App.Services;

/// <summary>
/// The PC's own player for Fenpo on the phones that offer CAPABILITY_MEDIA (Android both ways, the iPhone for the PC
/// tab only): what Windows plays goes to them as MediaState, and their MediaCommand controls it through the system
/// media sessions. Fenpo's own session (the phone's player shown in Windows) is left out.
/// </summary>
public sealed class PcMediaShare(PhoneChannel channel, ILogger<PcMediaShare> logger) : IHostedService
{
    private readonly PhoneChannel _channel = channel;
    private readonly ILogger<PcMediaShare> _logger = logger;
    private readonly SemaphoreSlim _sending = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly HashSet<GlobalSystemMediaTransportControlsSession> _watched = [];
    private string _ownId = string.Empty;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ownId = Windows.ApplicationModel.AppInfo.Current.AppUserModelId;
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (sender, e) => Follow();
            _manager.CurrentSessionChanged += (sender, e) => Follow();
            Follow();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The PC's media sessions are not available");
        }

        _channel.HelloReceived += (sender, hello) =>
        {
            if (hello.Envelope.Hello.Capabilities.Contains(Capability.Media))
            {
                _ = SendStateAsync();
            }
        };
        _channel.MessageReceived += (sender, message) =>
        {
            if (message.Envelope.BodyCase == Envelope.BodyOneofCase.MediaCommand)
            {
                _ = RunAsync(message);
            }
        };
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // The session that plays, else the current one; never Fenpo's own.
    private GlobalSystemMediaTransportControlsSession? Pick()
    {
        var sessions = _manager?.GetSessions().Where(s => s.SourceAppUserModelId != _ownId).ToList() ?? [];
        return sessions.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? sessions.FirstOrDefault(s => s.SourceAppUserModelId == _manager?.GetCurrentSession()?.SourceAppUserModelId)
            ?? sessions.FirstOrDefault();
    }

    private void Follow()
    {
        var session = Pick();

        if (!ReferenceEquals(session, _session))
        {
            _session = session;

            if (session is not null && _watched.Add(session))
            {
                session.MediaPropertiesChanged += (sender, e) => OnSessionChanged(sender);
                session.PlaybackInfoChanged += (sender, e) => OnSessionChanged(sender);
            }
        }

        _ = SendStateAsync();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession session)
    {
        // Another player started: follow it.
        if (!ReferenceEquals(session, _session) && session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            Follow();
            return;
        }

        if (ReferenceEquals(session, _session))
        {
            _ = SendStateAsync();
        }
    }

    private async Task SendStateAsync()
    {
        if (!_channel.AnyOffers(Capability.Media) || !await _sending.WaitAsync(0))
        {
            return;
        }

        try
        {
            var state = new MediaState { VolumePercent = SystemVolume.Get() ?? 0 };

            if (_session is { } session)
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                state.Active = !string.IsNullOrEmpty(properties?.Title);
                state.AppId = session.SourceAppUserModelId;
                state.AppName = AppName(session.SourceAppUserModelId);
                state.Title = properties?.Title ?? string.Empty;
                state.Artist = properties?.Artist ?? string.Empty;
                state.Album = properties?.AlbumTitle ?? string.Empty;
                state.Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                state.DurationMs = (ulong)Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalMilliseconds);
                state.PositionMs = (ulong)Math.Max(0, timeline.Position.TotalMilliseconds);
                state.PositionAt = Timestamp.FromDateTimeOffset(timeline.LastUpdatedTime);
                state.CanNext = playback.Controls.IsNextEnabled;
                state.CanPrevious = playback.Controls.IsPreviousEnabled;
                state.CanSeek = playback.Controls.IsPlaybackPositionEnabled;
            }

            await _channel.SendToEveryAsync(Capability.Media, new Envelope { MediaState = state });
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            _logger.LogDebug(exception, "Reading the PC's player failed");
        }
        finally
        {
            _sending.Release();
        }
    }

    private static string AppName(string appUserModelId)
    {
        try
        {
            return Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(appUserModelId).DisplayInfo.DisplayName;
        }
        catch (Exception exception) when (exception is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // Desktop apps without package identity: "Spotify.exe" reads well enough without the extension.
            return Path.GetFileNameWithoutExtension(appUserModelId);
        }
    }

    private async Task RunAsync(ChannelMessage message)
    {
        var command = message.Envelope.MediaCommand;

        // The PC's volume works without a player.
        if (command.Action == MediaAction.SetVolume && _session is null)
        {
            SystemVolume.Set(command.VolumePercent);
            return;
        }

        if (_session is not { } session)
        {
            await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.Unsupported, Message = "Nothing plays on the PC" } });
            return;
        }

        var done = command.Action switch
        {
            MediaAction.Play => await session.TryPlayAsync(),
            MediaAction.Pause => await session.TryPauseAsync(),
            MediaAction.Toggle => await session.TryTogglePlayPauseAsync(),
            MediaAction.Next => await session.TrySkipNextAsync(),
            MediaAction.Previous => await session.TrySkipPreviousAsync(),
            MediaAction.Seek => await session.TryChangePlaybackPositionAsync(TimeSpan.FromMilliseconds(command.SeekToMs).Ticks),
            MediaAction.SetVolume => SystemVolume.Set(command.VolumePercent),
            _ => false,
        };

        // The phone shows the new volume from the next state.
        if (done && command.Action == MediaAction.SetVolume)
        {
            _ = SendStateAsync();
        }

        _logger.LogInformation("Phone media command {Action} on the PC: {Done}", command.Action, done);

        if (!done)
        {
            await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.Unsupported, Message = $"The PC's player does not take {command.Action}" } });
        }
    }
}
