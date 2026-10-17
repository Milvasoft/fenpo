using Fenpo.Core.Media;

namespace Fenpo.App.Demo;

/// <summary>
/// A made-up song playing on the demo phone; the buttons pause and resume it.
/// </summary>
public sealed class DemoMediaSource : IMediaSource
{
    private NowPlaying _current = new()
    {
        PlayerName = DemoContent.Current.PlayerName,
        Title = DemoContent.Current.SongTitle,
        Artist = DemoContent.Current.SongArtist,
        Album = DemoContent.Current.SongAlbum,
        Duration = TimeSpan.FromSeconds(222),
        Elapsed = TimeSpan.FromSeconds(78),
        ElapsedAt = DateTimeOffset.Now,
        Rate = 1,
        State = PlaybackState.Playing,
        SupportedCommands = new HashSet<MediaCommand> { MediaCommand.TogglePlayPause, MediaCommand.NextTrack, MediaCommand.PreviousTrack },
    };

    /// <inheritdoc/>
    public event EventHandler<NowPlaying>? Changed;

    /// <inheritdoc/>
    public NowPlaying Current => _current;

    /// <inheritdoc/>
    public Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        if (command is MediaCommand.TogglePlayPause or MediaCommand.Play or MediaCommand.Pause)
        {
            var now = DateTimeOffset.Now;
            var playing = command == MediaCommand.Play || (command == MediaCommand.TogglePlayPause && _current.State != PlaybackState.Playing);
            _current = _current with
            {
                Elapsed = _current.PositionAt(now),
                ElapsedAt = now,
                Rate = playing ? 1 : 0,
                State = playing ? PlaybackState.Playing : PlaybackState.Paused,
            };
            Changed?.Invoke(this, _current);
        }

        return Task.FromResult(true);
    }
}
