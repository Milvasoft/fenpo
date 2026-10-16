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
    public void NoMeasurementValueIsIgnored()
    {
        var detector = new ProximityDetector();
        detector.AddSample(_start, -127);

        Assert.Null(detector.AverageDbm);
    }

    private static DateTimeOffset _now = _start;

    private static TimeSpan Now(ProximityDetector _) => _now - _start;
}
