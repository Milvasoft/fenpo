using Fenpo.Core.Input;

namespace Fenpo.Core.Tests.Input;

public sealed class HidReportsTests
{
    [Theory]
    [InlineData(0x1E, false, 0x04)] // A
    [InlineData(0x10, false, 0x14)] // Q
    [InlineData(0x0B, false, 0x27)] // 0
    [InlineData(0x1C, false, 0x28)] // Enter
    [InlineData(0x1C, true, 0x58)]  // keypad Enter
    [InlineData(0x4B, true, 0x50)]  // Left arrow
    [InlineData(0x4B, false, 0x5C)] // keypad 4
    [InlineData(0x56, false, 0x64)] // <> on a Turkish keyboard
    [InlineData(0x57, false, 0x44)] // F11
    public void KeysMapByPosition(int scanCode, bool extended, byte usage) => Assert.Equal(usage, HidReports.Usage(scanCode, extended));

    [Fact]
    public void ShiftAndAMakeAShiftedReport()
    {
        var keyboard = new HidKeyboardState();

        Assert.True(keyboard.Change(0x2A, false, down: true));
        Assert.True(keyboard.Change(0x1E, false, down: true));
        Assert.Equal(new byte[] { 0x02, 0, 0x04, 0, 0, 0, 0, 0 }, keyboard.Report());
        Assert.True(keyboard.Change(0x1E, false, down: false));
        Assert.Equal(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0 }, keyboard.Report());
    }

    [Fact]
    public void TheWindowsKeyIsTheCommandKey()
    {
        var keyboard = new HidKeyboardState();

        keyboard.Change(0x5B, true, down: true);

        Assert.Equal(0x08, keyboard.Report()[0]);
    }

    [Fact]
    public void ARepeatedKeyDoesNotChangeTheReport()
    {
        var keyboard = new HidKeyboardState();

        Assert.True(keyboard.Change(0x1E, false, down: true));
        Assert.False(keyboard.Change(0x1E, false, down: true));
    }

    [Fact]
    public void ALongMoveIsSplitAcrossReports()
    {
        var reports = HidReports.Mouse(300, -10, 0, 1).ToList();

        Assert.Equal(3, reports.Count);
        Assert.Equal(300, reports.Sum(r => (sbyte)r[1]));
        Assert.Equal(-10, reports.Sum(r => (sbyte)r[2]));
        Assert.All(reports, r => Assert.Equal(1, r[0]));
    }

    [Fact]
    public void AClickIsOneReport() => Assert.Single(HidReports.Mouse(0, 0, 0, 1));
}
