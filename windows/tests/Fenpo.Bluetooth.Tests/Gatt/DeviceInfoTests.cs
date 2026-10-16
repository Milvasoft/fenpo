using System.Text;
using Fenpo.Bluetooth.Gatt;

namespace Fenpo.Bluetooth.Tests.Gatt;

public sealed class DeviceInfoTests
{
    private static readonly IReadOnlyList<FixtureEntry> _reads = FixtureEntry.Load("device-info-battery-time.jsonl");

    [Fact]
    public void RecordedValuesParse()
    {
        Assert.Equal("iPhone14,5", Encoding.UTF8.GetString(_reads.First(e => e.Source == "Model Number").Data));
        Assert.Equal(40, _reads.First(e => e.Source == "Battery Level").Data[0]);
        Assert.True(CurrentTimeParser.TryParse(_reads.First(e => e.Source == "Current Time (char)").Data, out var time));
        Assert.Equal(new DateTime(2026, 10, 15, 23, 56, 1), time);
    }

    [Fact]
    public void ShortUuidsExpand() => Assert.Equal(new Guid("0000180f-0000-1000-8000-00805f9b34fb"), GattUuids.Battery);
}
