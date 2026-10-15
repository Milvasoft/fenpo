using Fenpo.Core.Calls;

namespace Fenpo.Core.Tests.Calls;

public sealed class CallLogTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 16, 14, 3, 0, TimeSpan.FromHours(3));

    [Fact]
    public void MissedAfterRingingReplacesTheEntry()
    {
        var log = new CallLog();
        log.Ringing("Annem", _now);

        log.Missed("Annem", _now.AddSeconds(30));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(CallKind.Missed, entry.Kind);
        Assert.Equal(_now, entry.Time);
    }

    [Fact]
    public void MissedWithoutRingingIsAdded()
    {
        var log = new CallLog();
        log.Ringing("Mert", _now);

        log.Missed("Annem", _now.AddSeconds(30));

        Assert.Equal(["Annem", "Mert"], log.Entries.Select(e => e.Name));
        Assert.Equal(CallKind.Incoming, log.Entries[1].Kind);
    }

    [Fact]
    public void RepeatedMissedReportIsIgnored()
    {
        var log = new CallLog();

        log.Missed("0532 000 44 18", _now);
        log.Missed("0532 000 44 18", _now.AddSeconds(5));

        Assert.Single(log.Entries);
    }

    [Fact]
    public void KeepsTheNewestUpToCapacity()
    {
        var log = new CallLog();

        for (var i = 0; i < CallLog.Capacity + 5; i++)
        {
            log.Ringing($"Arayan {i}", _now.AddMinutes(i));
        }

        Assert.Equal(CallLog.Capacity, log.Entries.Count);
        Assert.Equal($"Arayan {CallLog.Capacity + 4}", log.Entries[0].Name);
    }
}
