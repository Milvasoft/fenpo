using Fenpo.Bluetooth.Ams;
using Fenpo.Core.Media;

namespace Fenpo.Bluetooth.Tests.Ams;

public sealed class AmsReplayTests
{
    private static readonly IReadOnlyList<FixtureEntry> _session = FixtureEntry.Load("ams-commands.jsonl");

    [Fact]
    public void RegistrationsMatchTheRecording()
    {
        var writes = _session.Where(e => e.Source == "AMS Entity Update" && e.Kind == "write").Select(e => Convert.ToHexString(e.Data)).ToList();

        Assert.Equal(AmsProtocol.Registrations.Select(Convert.ToHexString), writes);
    }

    [Fact]
    public void UpdatesProduceTheFinalState()
    {
        var state = NowPlaying.Empty;
        var start = DateTimeOffset.Now;

        foreach (var entry in _session.Where(e => e.Source == "AMS Entity Update" && e.Kind == "notify"))
        {
            Assert.True(AmsEntityUpdate.TryParse(entry.Data, out var update));
            state = AmsProtocol.Apply(state, update, start.AddMilliseconds(entry.Ms));
        }

        Assert.Equal("YouTube Music", state.PlayerName);
        Assert.Equal("Örnek Sanatçı", state.Artist);
        Assert.StartsWith("Kara Bulut - Gece Yolculuğu", state.Title);
        Assert.Equal(224.901, state.Duration!.Value.TotalSeconds, 3);
        Assert.Equal(PlaybackState.Playing, state.State);
        Assert.Equal(1.0, state.Rate);
        Assert.Equal(0.4375, state.Volume);
    }

    [Fact]
    public void EmptyPlaybackInfoMeansPaused()
    {
        Assert.True(AmsEntityUpdate.TryParse([0, 1, 0, .. "0,,"u8], out var update));
        var state = AmsProtocol.Apply(NowPlaying.Empty, update, DateTimeOffset.Now);

        Assert.Equal(PlaybackState.Paused, state.State);
        Assert.Equal(0, state.Rate);
    }

    [Fact]
    public void SupportedCommandsParse()
    {
        var notifies = _session.Where(e => e.Source == "AMS Remote Command" && e.Kind == "notify").Select(e => AmsProtocol.ParseSupportedCommands(e.Data)).ToList();

        Assert.Equal([MediaCommand.VolumeUp, MediaCommand.VolumeDown], notifies[0].Order());
        Assert.Contains(MediaCommand.TogglePlayPause, notifies[^1]);
        Assert.Contains(MediaCommand.NextTrack, notifies[^1]);
    }
}
