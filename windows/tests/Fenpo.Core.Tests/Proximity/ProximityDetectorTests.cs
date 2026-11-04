using Fenpo.Core.Proximity;

namespace Fenpo.Core.Tests.Proximity;

public sealed class ProximityDetectorTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 15, 23, 55, 59, TimeSpan.FromHours(3));

    [Fact]
    public void WalkAwayRecordingLocksOnceAndComesBack()
    {
        // S1 recording: at the desk -50..-55 dBm, walking away -78..-92, link dropped at about 102 s,
        // advertisements gone until about 161 s, link back at about 166 s.
        var detector = new ProximityDetector();
        var transitions = new List<(double Seconds, ProximityState State)>();
        var locks = new List<double>();
        detector.StateChanged += (_, state) => transitions.Add((Now(detector).TotalSeconds, state));
        detector.LockRequested += (_, _) => locks.Add(Now(detector).TotalSeconds);

        var events = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rssi-walk-away.csv"))
            .Skip(1)
            .Select(line => line.Split(','))
            .Where(parts => parts[1] == "adv-bonded")
            .Select(parts => (Ms: int.Parse(parts[0]), Rssi: int.Parse(parts[2])))
            .ToList();

        detector.SetConnected(true, _start);
        var connectionEvents = new Queue<(int Ms, bool Connected)>([(102_000, false), (166_000, true)]);
        var sample = 0;

        for (var ms = 0; ms <= 250_000; ms += 250)
        {
            _now = _start.AddMilliseconds(ms);

            while (connectionEvents.Count > 0 && connectionEvents.Peek().Ms <= ms)
            {
                detector.SetConnected(connectionEvents.Dequeue().Connected, _now);
            }

            while (sample < events.Count && events[sample].Ms <= ms)
            {
                detector.AddSample(_start.AddMilliseconds(events[sample].Ms), events[sample].Rssi);
                sample++;
            }

            detector.Tick(_now);
        }

        var log = string.Join("; ", transitions.Select(t => $"{t.Seconds:F1}s {t.State}")) + $" | locks {string.Join(",", locks)}";
        Assert.True(locks.Count == 1, log);
        Assert.True(locks[0] is >= 110 and <= 125, log);
        Assert.Equal(ProximityState.Near, transitions[0].State);
        var leaving = transitions.First(t => t.State == ProximityState.Leaving);
        Assert.True(leaving.Seconds is >= 84 and <= 92, log);
        Assert.Equal(ProximityState.Near, transitions[^1].State);
        Assert.True(transitions[^1].Seconds is >= 160 and <= 170, log);
        Assert.DoesNotContain(transitions, t => t.State == ProximityState.Leaving && t.Seconds < 80);
    }

    [Fact]
    public void ShortDipDoesNotLock()
    {
        var detector = new ProximityDetector();
        var locked = false;
        detector.LockRequested += (_, _) => locked = true;
        detector.SetConnected(true, _start);

        for (var s = 0; s < 120; s++)
        {
            var rssi = s is >= 40 and < 50 ? -90 : -52;
            detector.AddSample(_start.AddSeconds(s), rssi);
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.False(locked);
        Assert.Equal(ProximityState.Near, detector.State);
    }

    [Fact]
    public void DisconnectWithoutSignalLocksAfterDelay()
    {
        var detector = new ProximityDetector { Delay = TimeSpan.FromSeconds(30) };
        var locks = 0;
        detector.LockRequested += (_, _) => locks++;
        detector.SetConnected(true, _start);
        detector.AddSample(_start, -50);
        detector.SetConnected(false, _start.AddSeconds(1));

        for (var s = 1; s <= 60; s++)
        {
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(1, locks);
        Assert.Equal(ProximityState.Away, detector.State);
    }

    [Fact]
    public void LinkDroppingBeforeAnyMeasurementDoesNotLock()
    {
        // Owner's log: back in range by a fresh link alone, no advertisement on it, the link drops within a second.
        var detector = new ProximityDetector { Delay = TimeSpan.FromSeconds(30) };
        var locks = 0;
        detector.LockRequested += (_, _) => locks++;
        detector.SetConnected(true, _start);
        detector.AddSample(_start, -50);
        detector.SetConnected(false, _start.AddSeconds(1));

        for (var s = 1; s <= 60; s++)
        {
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(1, locks);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            var connectedAt = _start.AddSeconds(100 + (cycle * 25));
            detector.SetConnected(true, connectedAt);
            Assert.Equal(ProximityState.Near, detector.State);
            detector.SetConnected(false, connectedAt.AddSeconds(1));
        }

        for (var s = 126; s <= 300; s++)
        {
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(1, locks);
        Assert.Equal(ProximityState.Near, detector.State);
    }

    [Fact]
    public void AverageBackAboveThresholdRestartsDelay()
    {
        // Weak (-85) from 10 s, back to -79 (above the -80 threshold, below the -75 needed to count as near) from
        // 20 s to 40 s, weak again from 41 s: the delay starts over and the lock comes 30 s after the last -79.
        var detector = new ProximityDetector { Delay = TimeSpan.FromSeconds(30) };
        var states = new List<ProximityState>();
        var locks = new List<int>();
        var second = 0;
        detector.StateChanged += (_, state) => states.Add(state);
        detector.LockRequested += (_, _) => locks.Add(second);
        detector.SetConnected(true, _start);

        for (second = 0; second <= 120; second++)
        {
            var rssi = second switch
            {
                < 10 => -50,
                < 20 => -85,
                <= 40 => -79,
                _ => -85,
            };
            detector.AddSample(_start.AddSeconds(second), rssi);
            detector.Tick(_start.AddSeconds(second));
        }

        Assert.Equal([ProximityState.Near, ProximityState.Leaving, ProximityState.Away], states);
        Assert.Single(locks);
        Assert.InRange(locks[0], 70, 80);
    }

    [Fact]
    public void LiveLinkWithoutSamplesDoesNotRunOutToAway()
    {
        // Weak from 10 s, then no advertisements while the link stays up: not evidence of leaving.
        var detector = new ProximityDetector { Delay = TimeSpan.FromSeconds(30) };
        var locks = 0;
        detector.LockRequested += (_, _) => locks++;
        detector.SetConnected(true, _start);

        for (var s = 0; s < 20; s++)
        {
            detector.AddSample(_start.AddSeconds(s), s < 10 ? -50 : -85);
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(ProximityState.Leaving, detector.State);

        for (var s = 20; s <= 120; s++)
        {
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(0, locks);
        Assert.Equal(ProximityState.Near, detector.State);

        // The phone was measured and went silent; when the link then drops too, that is a walk-away.
        detector.SetConnected(false, _start.AddSeconds(121));

        for (var s = 121; s <= 200; s++)
        {
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(1, locks);
        Assert.Equal(ProximityState.Away, detector.State);
    }

    [Fact]
    public void ConnectedFromEitherChannelKeepsThePhoneNearUntilBothGo()
    {
        // The iPhone's service link flaps while Fenpo on it stays on the Bluetooth channel. The service reports
        // "connected" while either is up, again on every change, so the detector sees no drop: silence alone never
        // starts the delay, and the repeated reports do not count as fresh links that forget the measurement.
        var detector = new ProximityDetector { Delay = TimeSpan.FromSeconds(30) };
        var locks = 0;
        detector.LockRequested += (_, _) => locks++;
        detector.SetConnected(true, _start);
        detector.AddSample(_start, -50);

        for (var s = 0; s <= 120; s++)
        {
            detector.SetConnected(true, _start.AddSeconds(s));
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(0, locks);
        Assert.Equal(ProximityState.Near, detector.State);

        // Both gone: the phone was measured on this link and is silent now, so it locks once after the delay.
        detector.SetConnected(false, _start.AddSeconds(121));

        for (var s = 121; s <= 200; s++)
        {
            detector.Tick(_start.AddSeconds(s));
        }

        Assert.Equal(1, locks);
        Assert.Equal(ProximityState.Away, detector.State);
    }

    [Fact]
    public void NoMeasurementValueIsIgnored()
    {
        var detector = new ProximityDetector();
        detector.AddSample(_start, -127);

        Assert.Null(detector.AverageDbm);
    }

    private static DateTimeOffset _now = _start;

    private static TimeSpan Now(ProximityDetector _) => _now - _start;
}
