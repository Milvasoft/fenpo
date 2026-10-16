namespace Fenpo.Core.Calls;

/// <summary>
/// How a call ended up, as far as the PC can tell.
/// </summary>
public enum CallKind
{
    /// <summary>Rang and was answered or declined on either device.</summary>
    Incoming,

    /// <summary>The phone reported it as missed.</summary>
    Missed,
}

/// <summary>
/// One call seen while Fenpo was running.
/// </summary>
/// <param name="Name">Caller name or number as the phone showed it.</param>
/// <param name="Kind">Incoming or missed.</param>
/// <param name="Time">When it rang.</param>
public sealed record CallEntry(string Name, CallKind Kind, DateTimeOffset Time);

/// <summary>
/// Recent calls. iPhones share calls only as notifications (ANCS), so the list holds the calls that rang while Fenpo
/// was running, newest first, at most <see cref="Capacity"/>.
/// </summary>
public sealed class CallLog
{
    /// <summary>
    /// Maximum number of entries kept.
    /// </summary>
    public const int Capacity = 50;

    private static readonly TimeSpan _missedWindow = TimeSpan.FromMinutes(3);

    private readonly Lock _lock = new();
    private readonly List<CallEntry> _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="CallLog"/> class.
    /// </summary>
    /// <param name="entries">Saved entries.</param>
    public CallLog(IEnumerable<CallEntry>? entries = null)
    {
        _entries = [.. (entries ?? []).OrderByDescending(e => e.Time).Take(Capacity)];
    }

    /// <summary>
    /// Raised after the list changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the entries, newest first.
    /// </summary>
    public IReadOnlyList<CallEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>
    /// Records a ringing call.
    /// </summary>
    /// <param name="name">Caller.</param>
    /// <param name="time">When it rang.</param>
    public void Ringing(string name, DateTimeOffset time) => Change(entries => entries.Insert(0, new CallEntry(name, CallKind.Incoming, time)));

    /// <summary>
    /// Records a missed call. A call from the same caller that rang a moment before becomes missed instead of being
    /// listed twice.
    /// </summary>
    /// <param name="name">Caller.</param>
    /// <param name="time">When the phone reported it.</param>
    public void Missed(string name, DateTimeOffset time) => Change(entries =>
    {
        var index = entries.FindIndex(e => e.Name == name && e.Kind == CallKind.Incoming && time - e.Time >= TimeSpan.Zero && time - e.Time < _missedWindow);

        if (index >= 0)
        {
            entries[index] = entries[index] with { Kind = CallKind.Missed };
        }
        else if (!entries.Exists(e => e.Name == name && e.Kind == CallKind.Missed && (time - e.Time).Duration() < _missedWindow))
        {
            entries.Insert(0, new CallEntry(name, CallKind.Missed, time));
        }
    });

    private void Change(Action<List<CallEntry>> change)
    {
        lock (_lock)
        {
            change(_entries);
            _entries.Sort((a, b) => b.Time.CompareTo(a.Time));

            if (_entries.Count > Capacity)
            {
                _entries.RemoveRange(Capacity, _entries.Count - Capacity);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
