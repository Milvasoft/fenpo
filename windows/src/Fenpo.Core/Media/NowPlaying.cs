namespace Fenpo.Core.Media;

/// <summary>
/// Playback state of the phone's current player.
/// </summary>
public enum PlaybackState
{
    /// <summary>Paused or stopped.</summary>
    Paused,

    /// <summary>Playing.</summary>
    Playing,

    /// <summary>Rewinding.</summary>
    Rewinding,

    /// <summary>Fast forwarding.</summary>
    FastForwarding,
}

/// <summary>
/// Remote commands the phone's player can accept. Values match AMS RemoteCommandID.
/// </summary>
public enum MediaCommand
{
    /// <summary>Play.</summary>
    Play = 0,

    /// <summary>Pause.</summary>
    Pause = 1,

    /// <summary>Toggle play and pause.</summary>
    TogglePlayPause = 2,

    /// <summary>Next track.</summary>
    NextTrack = 3,

    /// <summary>Previous track.</summary>
    PreviousTrack = 4,

    /// <summary>Volume up.</summary>
    VolumeUp = 5,

    /// <summary>Volume down.</summary>
    VolumeDown = 6,

    /// <summary>Advance repeat mode.</summary>
    AdvanceRepeatMode = 7,

    /// <summary>Advance shuffle mode.</summary>
    AdvanceShuffleMode = 8,

    /// <summary>Skip forward.</summary>
    SkipForward = 9,

    /// <summary>Skip backward.</summary>
    SkipBackward = 10,

    /// <summary>Like the track.</summary>
    LikeTrack = 11,

    /// <summary>Dislike the track.</summary>
    DislikeTrack = 12,

    /// <summary>Bookmark the track.</summary>
    BookmarkTrack = 13,
}

/// <summary>
/// What the phone is playing. Every field may be missing; the phone sends them one by one.
/// </summary>
public sealed record NowPlaying
{
    /// <summary>
    /// Gets an empty state (nothing known yet).
    /// </summary>
    public static NowPlaying Empty { get; } = new();

    /// <summary>
    /// Gets the player app's name, e.g. "Music".
    /// </summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the track title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets the artist.
    /// </summary>
    public string Artist { get; init; } = string.Empty;

    /// <summary>
    /// Gets the album.
    /// </summary>
    public string Album { get; init; } = string.Empty;

    /// <summary>
    /// Gets the track length.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the position at <see cref="ElapsedAt"/>.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Gets when <see cref="Elapsed"/> was reported; the current position advances from there at <see cref="Rate"/>.
    /// </summary>
    public DateTimeOffset ElapsedAt { get; init; }

    /// <summary>
    /// Gets the playback rate (1 while playing, 0 while paused).
    /// </summary>
    public double Rate { get; init; }

    /// <summary>
    /// Gets the playback state.
    /// </summary>
    public PlaybackState State { get; init; }

    /// <summary>
    /// Gets the player volume from 0 to 1, if known.
    /// </summary>
    public double? Volume { get; init; }

    /// <summary>
    /// Gets the commands the player accepts right now (S1: only volume while nothing plays).
    /// </summary>
    public IReadOnlySet<MediaCommand> SupportedCommands { get; init; } = new HashSet<MediaCommand>();

    /// <summary>
    /// Gets a value indicating whether there is anything to show.
    /// </summary>
    public bool HasTrack => !string.IsNullOrEmpty(Title);

    /// <summary>
    /// Gets the position now, extrapolated from the last report.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <returns>The position, clamped to the duration.</returns>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var position = Elapsed + ((now - ElapsedAt) * Rate);

        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return Duration is { } duration && position > duration ? duration : position;
    }
}
