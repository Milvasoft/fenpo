namespace Fenpo.Core.Proximity;

/// <summary>
/// Where the phone is relative to the PC.
/// </summary>
public enum ProximityState
{
    /// <summary>Nothing heard yet.</summary>
    Unknown,

    /// <summary>Close to the PC.</summary>
    Near,

    /// <summary>Signal weak, no signal or disconnected; the lock delay is running.</summary>
    Leaving,

    /// <summary>Stayed away for the whole delay; a lock was requested.</summary>
    Away,
}

/// <summary>
/// Decides when the phone has left (DECISIONS #29): a 5-second moving average of advertisement RSSI below the
/// threshold, no signal at all, or a dropped link starts the delay; staying away for the whole delay requests one lock.
/// Coming back (average above threshold plus hysteresis, or reconnecting) re-arms it. Pure logic: callers pass the
/// time with every call, so the S1 walk-away recording can be replayed in tests.
/// </summary>
public sealed class ProximityDetector
{
    private readonly Queue<(DateTimeOffset At, int Rssi)> _samples = new();
    private DateTimeOffset? _lastSample;
    private DateTimeOffset _leavingSince;
    private bool _connected;

    /// <summary>
    /// Raised when <see cref="State"/> changes.
    /// </summary>
    public event EventHandler<ProximityState>? StateChanged;

    /// <summary>
    /// Raised once per away period, when the phone stayed away for <see cref="Delay"/>.
    /// </summary>
    public event EventHandler? LockRequested;

    /// <summary>
    /// Gets or sets the averaged RSSI below which the phone counts as away.
    /// </summary>
    public int ThresholdDbm { get; set; } = -80;

    /// <summary>
    /// Gets or sets how much stronger than the threshold the average must be to count as back.
    /// </summary>
    public int HysteresisDb { get; set; } = 5;

    /// <summary>
    /// Gets or sets the averaging window.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how long the phone has to stay away before a lock.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long without any advertisement counts as no signal.
    /// </summary>
    public TimeSpan SignalTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the current state.
    /// </summary>
    public ProximityState State { get; private set; } = ProximityState.Unknown;

    /// <summary>
    /// Gets the current moving average, or null without recent samples.
    /// </summary>
    public double? AverageDbm => _samples.Count == 0 ? null : _samples.Average(s => s.Rssi);

    /// <summary>
    /// Adds an advertisement RSSI sample from the phone.
    /// </summary>
    /// <param name="at">When it was received.</param>
    /// <param name="rssi">Signal strength in dBm.</param>
    public void AddSample(DateTimeOffset at, int rssi)
    {
        // -127 means "no measurement" (S1 saw it on scan responses).
        if (rssi <= -127)
        {
            return;
        }

        _samples.Enqueue((at, rssi));
        _lastSample = at;
        Evaluate(at);
    }

    /// <summary>
    /// Reports the Bluetooth link state.
    /// </summary>
    /// <param name="connected">True when connected.</param>
    /// <param name="at">When it changed.</param>
    public void SetConnected(bool connected, DateTimeOffset at)
    {
        var reconnected = connected && !_connected;
        _connected = connected;

        // Some adapters report few advertisements while connected; a fresh link is proof enough of being back.
        if (reconnected && State is ProximityState.Leaving or ProximityState.Away && !(AverageDbm < ThresholdDbm))
        {
            SetState(ProximityState.Near);
        }

        Evaluate(at);
    }

    /// <summary>
    /// Re-evaluates timers; call about once a second.
    /// </summary>
    /// <param name="now">Current time.</param>
    public void Tick(DateTimeOffset now) => Evaluate(now);

    private void Evaluate(DateTimeOffset now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().At > Window)
        {
            _samples.Dequeue();
        }

        var average = AverageDbm;
        var noSignal = _lastSample is null || now - _lastSample > SignalTimeout;
        var weak = average is { } a && a < ThresholdDbm;
        var strong = average is { } b && b >= ThresholdDbm + HysteresisDb;

        switch (State)
        {
            case ProximityState.Unknown:
                if (strong || (_connected && average is not null && !weak))
                {
                    SetState(ProximityState.Near);
                }

                break;
            case ProximityState.Near:
                if (weak || (noSignal && !_connected))
                {
                    _leavingSince = now;
                    SetState(ProximityState.Leaving);
                }

                break;
            case ProximityState.Leaving:
                // Only a clearly strong average counts as back, so the state does not flap around the threshold.
                if (strong)
                {
                    SetState(ProximityState.Near);
                }
                else if (now - _leavingSince >= Delay)
                {
                    SetState(ProximityState.Away);
                    LockRequested?.Invoke(this, EventArgs.Empty);
                }

                break;
            case ProximityState.Away:
                if (strong)
                {
                    SetState(ProximityState.Near);
                }

                break;
        }
    }

    private void SetState(ProximityState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
