using Fenpo.Core.Notifications;

namespace Fenpo.Core.Tests.Notifications;

public sealed class NotificationMirrorStateTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 16, 17, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void NewKeyIsAddedAndANewerSeqModifies()
    {
        var state = new NotificationMirrorState();

        Assert.Equal(MirrorDecision.Add, state.OnPosted("k", 10, preExisting: false, _now));
        Assert.Equal(MirrorDecision.Modify, state.OnPosted("k", 11, preExisting: false, _now));
    }

    [Fact]
    public void OlderOrRepeatedSeqIsIgnored()
    {
        var state = new NotificationMirrorState();
        state.OnPosted("k", 10, preExisting: false, _now);

        // The same update over the second channel, or an older one that was overtaken.
        Assert.Equal(MirrorDecision.Ignore, state.OnPosted("k", 10, preExisting: false, _now));
        Assert.Equal(MirrorDecision.Ignore, state.OnPosted("k", 9, preExisting: false, _now));
    }

    [Fact]
    public void LateUpdateCannotBringARemovedKeyBack()
    {
        var state = new NotificationMirrorState();
        state.OnPosted("k", 10, preExisting: false, _now);

        Assert.True(state.OnRemoved("k", 12, _now));
        Assert.Equal(MirrorDecision.Ignore, state.OnPosted("k", 11, preExisting: false, _now));
        Assert.Equal(MirrorDecision.Add, state.OnPosted("k", 13, preExisting: false, _now));
    }

    [Fact]
    public void RemovalOlderThanTheLastUpdateIsIgnored()
    {
        var state = new NotificationMirrorState();
        state.OnPosted("k", 10, preExisting: false, _now);

        Assert.False(state.OnRemoved("k", 9, _now));
        Assert.Contains("k", state.Shown);
    }

    [Fact]
    public void RemovedKeysAreForgottenAfterAFewMinutes()
    {
        var state = new NotificationMirrorState();
        state.OnPosted("k", 10, preExisting: false, _now);
        state.OnRemoved("k", 12, _now);

        Assert.Equal(MirrorDecision.Add, state.OnPosted("k", 11, preExisting: false, _now.AddMinutes(6)));
    }

    [Fact]
    public void SnapshotMessagesCountEvenWithAnOlderSeq()
    {
        // The phone's clock went back and its process restarted: the snapshot after Hello is authoritative.
        var state = new NotificationMirrorState();
        state.OnPosted("k", 1_000, preExisting: false, _now);
        state.BeginSnapshot();

        Assert.Equal(MirrorDecision.Modify, state.OnPosted("k", 500, preExisting: true, _now));
        Assert.Equal(MirrorDecision.Modify, state.OnPosted("k", 501, preExisting: false, _now));
    }

    [Fact]
    public void SnapshotEndDropsKeysItDidNotMention()
    {
        var state = new NotificationMirrorState();
        state.OnPosted("kept", 1, preExisting: false, _now);
        state.OnPosted("gone", 2, preExisting: false, _now);
        state.BeginSnapshot();
        state.OnPosted("kept", 3, preExisting: true, _now);
        state.OnPosted("new", 4, preExisting: true, _now);

        var dropped = state.EndSnapshot();

        Assert.Equal(["gone"], dropped);
        Assert.Equal(["kept", "new"], state.Shown.Order());
        Assert.Empty(state.EndSnapshot());
    }

    [Fact]
    public void AfterADisconnectTheSnapshotAddsAgain()
    {
        var state = new NotificationMirrorState();
        state.OnPosted("k", 1, preExisting: false, _now);
        state.Disconnect();
        state.BeginSnapshot();

        Assert.Equal(MirrorDecision.Add, state.OnPosted("k", 1, preExisting: true, _now));
    }
}
