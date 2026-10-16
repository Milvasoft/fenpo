namespace Fenpo.Core.Media;

/// <summary>
/// Several phone players as one (the iPhone through AMS, an Android phone through the Fenpo channel): what shows is
/// the source that started playing last, or the one that still has a track when the other has none. Commands go to
/// the source that shows.
/// </summary>
public sealed class CombinedMediaSource : IMediaSource
{
    private readonly IReadOnlyList<IMediaSource> _sources;
    private readonly Lock _lock = new();
    private IMediaSource _active;

    /// <summary>
    /// Initializes a new instance of the <see cref="CombinedMediaSource"/> class.
    /// </summary>
    /// <param name="sources">Sources, the default first.</param>
    public CombinedMediaSource(IReadOnlyList<IMediaSource> sources)
    {
        _sources = sources;
        _active = sources[0];

        foreach (var source in sources)
        {
            source.Changed += (sender, nowPlaying) => OnChanged(source, nowPlaying);
        }
    }

    /// <inheritdoc/>
    public event EventHandler<NowPlaying>? Changed;

    /// <inheritdoc/>
    public NowPlaying Current
    {
        get
        {
            lock (_lock)
            {
                return _active.Current;
            }
        }
    }

    /// <inheritdoc/>
    public Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        IMediaSource active;

        lock (_lock)
        {
            active = _active;
        }

        return active.SendAsync(command, cancellationToken);
    }

    private void OnChanged(IMediaSource source, NowPlaying nowPlaying)
    {
        NowPlaying shown;

        lock (_lock)
        {
            var startedPlaying = nowPlaying.State == PlaybackState.Playing && _active.Current.State != PlaybackState.Playing;
            var activeIsEmpty = !_active.Current.HasTrack && nowPlaying.HasTrack;

            if (source != _active && (startedPlaying || activeIsEmpty))
            {
                _active = source;
            }
            else if (source == _active && !nowPlaying.HasTrack && _sources.FirstOrDefault(s => s != source && s.Current.HasTrack) is { } other)
            {
                // The shown player stopped; another one still has something.
                _active = other;
            }
            else if (source != _active)
            {
                return;
            }

            shown = _active.Current;
        }

        Changed?.Invoke(this, shown);
    }
}
