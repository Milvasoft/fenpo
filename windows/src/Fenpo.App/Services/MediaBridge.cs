using Fenpo.Core.Media;
using Fenpo.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Media;
using Windows.Media.Playback;

namespace Fenpo.App.Services;

/// <summary>
/// Puts the phone's player into Windows: the media flyout and the keyboard media keys control the phone through the
/// System Media Transport Controls of a silent MediaPlayer that never plays anything itself.
/// </summary>
public sealed class MediaBridge(IMediaSource source, ISettingsStore settings, ILogger<MediaBridge> logger) : IHostedService
{
    private readonly IMediaSource _source = source;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<MediaBridge> _logger = logger;
    private MediaPlayer? _player;
    private SystemMediaTransportControls? _controls;
    private string _shownTrack = string.Empty;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _player = new MediaPlayer();
        _player.CommandManager.IsEnabled = false;
        _controls = _player.SystemMediaTransportControls;
        _controls.ButtonPressed += (sender, args) => _ = OnButtonAsync(args.Button);
        _source.Changed += (_, nowPlaying) => Update(nowPlaying);
        _settings.Changed += (_, _) => Update(_source.Current);
        Update(_source.Current);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _player?.Dispose();
        _player = null;
        return Task.CompletedTask;
    }

    private void Update(NowPlaying nowPlaying)
    {
        if (_controls is null)
        {
            return;
        }

        var visible = _settings.Current.MediaControl && nowPlaying.HasTrack;
        _controls.IsEnabled = visible;

        if (!visible)
        {
            _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            _shownTrack = string.Empty;
            return;
        }

        var commands = nowPlaying.SupportedCommands;
        _controls.IsPlayEnabled = commands.Contains(MediaCommand.Play) || commands.Contains(MediaCommand.TogglePlayPause);
        _controls.IsPauseEnabled = commands.Contains(MediaCommand.Pause) || commands.Contains(MediaCommand.TogglePlayPause);
        _controls.IsNextEnabled = commands.Contains(MediaCommand.NextTrack);
        _controls.IsPreviousEnabled = commands.Contains(MediaCommand.PreviousTrack);
        _controls.PlaybackStatus = nowPlaying.State == PlaybackState.Playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

        var track = $"{nowPlaying.Title}\n{nowPlaying.Artist}\n{nowPlaying.Album}";

        if (track != _shownTrack)
        {
            _shownTrack = track;
            var updater = _controls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = nowPlaying.Title;
            updater.MusicProperties.Artist = nowPlaying.Artist;
            updater.MusicProperties.AlbumTitle = nowPlaying.Album;
            updater.Update();
        }

        if (nowPlaying.Duration is { } duration)
        {
            _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                EndTime = duration,
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = duration,
                Position = nowPlaying.PositionAt(DateTimeOffset.Now),
            });
        }
    }

    private async Task OnButtonAsync(SystemMediaTransportControlsButton button)
    {
        var command = button switch
        {
            SystemMediaTransportControlsButton.Play => MediaCommand.Play,
            SystemMediaTransportControlsButton.Pause => MediaCommand.Pause,
            SystemMediaTransportControlsButton.Next => MediaCommand.NextTrack,
            SystemMediaTransportControlsButton.Previous => MediaCommand.PreviousTrack,
            _ => (MediaCommand?)null,
        };

        if (command is { } value)
        {
            var sent = await _source.SendAsync(value);
            _logger.LogDebug("Media key {Button} -> {Command}: {Sent}", button, value, sent);
        }
    }
}
