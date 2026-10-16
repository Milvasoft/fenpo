namespace Fenpo.Core.Notifications;

/// <summary>
/// What to do with a mirrored notification message.
/// </summary>
public enum MirrorDecision
{
    /// <summary>Older than what the PC already has (or for a removed key): drop it.</summary>
    Ignore,

    /// <summary>A key the PC does not show yet.</summary>
    Add,

    /// <summary>An update of a key the PC shows.</summary>
    Modify,
}

/// <summary>
/// Order and snapshot bookkeeping for notifications an Android phone mirrors over two channels
/// (protocol/README.md, Notification mirroring): a message counts only when its <c>seq</c> is newer than the last one
/// for its key, removed keys are remembered for a few minutes so a late update cannot bring them back, snapshot
/// messages (<c>pre_existing</c>) always count (they come after a Hello, possibly from a phone whose clock went
/// back), and the end of a snapshot drops the keys it did not mention.
/// </summary>
public sealed class NotificationMirrorState
{
    private static readonly TimeSpan _removedMemory = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, ulong> _lastSeq = [];
    private readonly Dictionary<string, (ulong Seq, DateTimeOffset At)> _removed = [];
    private readonly HashSet<string> _shown = [];
    private HashSet<string>? _snapshot;

    /// <summary>
    /// Gets the keys the PC shows now.
    /// </summary>
    public IReadOnlyCollection<string> Shown
    {
        get
        {
            lock (_lock)
            {
                return [.. _shown];
            }
        }
    }

    /// <summary>
    /// Decides about a posted or updated notification.
    /// </summary>
    /// <param name="key">Notification key.</param>
    /// <param name="seq">Message sequence.</param>
    /// <param name="preExisting">Part of a snapshot.</param>
    /// <param name="now">Current time.</param>
    /// <returns>What to do.</returns>
    public MirrorDecision OnPosted(string key, ulong seq, bool preExisting, DateTimeOffset now)
    {
        lock (_lock)
        {
            Forget(now);

            if (preExisting)
            {
                _snapshot?.Add(key);
                _removed.Remove(key);
            }
            else if ((_lastSeq.TryGetValue(key, out var last) && seq <= last) || (_removed.TryGetValue(key, out var removed) && seq <= removed.Seq))
            {
                return MirrorDecision.Ignore;
            }

            _lastSeq[key] = seq;
            return _shown.Add(key) ? MirrorDecision.Add : MirrorDecision.Modify;
        }
    }

    /// <summary>
    /// Decides about a removal.
    /// </summary>
    /// <param name="key">Notification key.</param>
    /// <param name="seq">Message sequence.</param>
    /// <param name="now">Current time.</param>
    /// <returns>True when the PC should remove it.</returns>
    public bool OnRemoved(string key, ulong seq, DateTimeOffset now)
    {
        lock (_lock)
        {
            Forget(now);

            if (_lastSeq.TryGetValue(key, out var last) && seq <= last)
            {
                return false;
            }

            _lastSeq.Remove(key);
            _removed[key] = (seq, now);
            return _shown.Remove(key);
        }
    }

    /// <summary>
    /// Starts collecting the keys of a snapshot (after the phone's Hello).
    /// </summary>
    public void BeginSnapshot()
    {
        lock (_lock)
        {
            _snapshot = [];
        }
    }

    /// <summary>
    /// Ends a snapshot.
    /// </summary>
    /// <returns>Keys the PC showed that the snapshot did not mention; they are gone on the phone.</returns>
    public IReadOnlyList<string> EndSnapshot()
    {
        lock (_lock)
        {
            if (_snapshot is not { } seen)
            {
                return [];
            }

            _snapshot = null;
            var gone = _shown.Where(k => !seen.Contains(k)).ToList();

            foreach (var key in gone)
            {
                _shown.Remove(key);
                _lastSeq.Remove(key);
            }

            return gone;
        }
    }

    /// <summary>
    /// Forgets what is shown after the phone left (the next snapshot brings it back); the order memory stays.
    /// </summary>
    public void Disconnect()
    {
        lock (_lock)
        {
            _shown.Clear();
            _snapshot = null;
        }
    }

    private void Forget(DateTimeOffset now)
    {
        foreach (var key in _removed.Where(r => now - r.Value.At > _removedMemory).Select(r => r.Key).ToList())
        {
            _removed.Remove(key);
        }
    }
}
