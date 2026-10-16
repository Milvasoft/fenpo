using Fenpo.Core.Media;

namespace Fenpo.Core.Tests.Media;

public sealed class NowPlayingTests
{
    [Fact]
    public void PositionAdvancesWhilePlayingAndClampsToDuration()
    {
        var at = DateTimeOffset.Now;
        var playing = new NowPlaying { Title = "Gece Yolculuğu", Elapsed = TimeSpan.FromSeconds(38.4), ElapsedAt = at, Rate = 1, Duration = TimeSpan.FromSeconds(60) };

        Assert.Equal(48.4, playing.PositionAt(at.AddSeconds(10)).TotalSeconds, 3);
        Assert.Equal(60, playing.PositionAt(at.AddSeconds(100)).TotalSeconds, 3);
        Assert.Equal(38.4, (playing with { Rate = 0 }).PositionAt(at.AddSeconds(10)).TotalSeconds, 3);
    }
}
