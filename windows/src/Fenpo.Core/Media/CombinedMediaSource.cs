namespace Fenpo.Core.Media;

/// <summary>
/// The players of several phones (the iPhone through AMS, an Android phone through the Fenpo channel) as one: what
/// shows and takes commands is the player of the phone in use (<see cref="Use"/>).
/// </summary>
public sealed class CombinedMediaSource : IMediaSource
{
    private readonly IReadOnlyList<IMediaSource> _sources;
    private readonly Lock _lock = new();
    private IMediaSource _active;

    /// <summary>
    /// Initializes a new instance of the <see cref="CombinedMediaSource"/> class.
    /// </summary>
    /// <param name="sources">Sources, the one in use first.</param>
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

    /// <summary>
    /// Shows and controls one source's player.
    /// </summary>
    /// <param name="index">Index in the list given to the constructor.</param>
    public void Use(int index)
    {
        NowPlaying shown;

        lock (_lock)
        {
            if (_active == _sources[index])
            {
                return;
            }

            _active = _sources[index];
            shown = _active.Current;
        }

        Changed?.Invoke(this, shown);
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
        lock (_lock)
        {
            if (source != _active)
            {
                return;
            }
        }

        Changed?.Invoke(this, nowPlaying);
    }
}
