using Fenpo.Core.Calls;

namespace Fenpo.Core.Tests.Calls;

public sealed class FullScreenRuleTests
{
    private static readonly ScreenRect _monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void ABorderlessGameFillsTheMonitor()
    {
        Assert.True(FullScreenRule.FillsMonitor(new ScreenRect(0, 0, 1920, 1080), _monitor, maximizedWithTitleBar: false));
    }

    [Fact]
    public void AGameOnTheSecondMonitorFillsThatMonitor()
    {
        Assert.True(FullScreenRule.FillsMonitor(new ScreenRect(1920, 0, 4480, 1440), new ScreenRect(1920, 0, 4480, 1440), maximizedWithTitleBar: false));
    }

    [Fact]
    public void AMaximizedWindowAboveTheTaskbarDoesNot()
    {
        Assert.False(FullScreenRule.FillsMonitor(new ScreenRect(-8, -8, 1928, 1048), _monitor, maximizedWithTitleBar: true));
    }

    [Fact]
    public void AMaximizedWindowWithAHiddenTaskbarDoesNot()
    {
        Assert.False(FullScreenRule.FillsMonitor(new ScreenRect(-8, -8, 1928, 1088), _monitor, maximizedWithTitleBar: true));
    }

    [Fact]
    public void AnOrdinaryWindowDoesNot()
    {
        Assert.False(FullScreenRule.FillsMonitor(new ScreenRect(100, 100, 900, 700), _monitor, maximizedWithTitleBar: false));
    }
}
