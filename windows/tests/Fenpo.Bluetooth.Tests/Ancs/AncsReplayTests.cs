using Fenpo.Bluetooth.Ancs;
using Fenpo.Core.Notifications;

namespace Fenpo.Bluetooth.Tests.Ancs;

public sealed class AncsReplayTests
{
    private static readonly IReadOnlyList<FixtureEntry> _session = FixtureEntry.Load("ancs-preexisting-and-actions.jsonl");

    [Fact]
    public void NotificationSourceEventsParse()
    {
        var events = _session.Where(e => e.Source == "ANCS Notification Source").Select(e => Parse(e.Data)).ToList();

        Assert.Equal(8, events.Count);
        var first = events[0];
        Assert.Equal(AncsEventId.Added, first.EventId);
        Assert.Equal(0u, first.Uid);
        Assert.Equal(NotificationCategory.Other, first.Category);
        Assert.True(first.Flags.HasFlag(NotificationFlags.PreExisting));
        Assert.True(first.Flags.HasFlag(NotificationFlags.Silent));
        Assert.True(first.HasNegativeAction);
        Assert.False(first.HasPositiveAction);
        Assert.Contains(events, e => e.EventId == AncsEventId.Removed && e.Uid == 5 && e.Category == NotificationCategory.Email);
        Assert.Equal(6, events.Count(e => e.EventId == AncsEventId.Added));
    }

    [Fact]
    public void ControlPointCommandsMatchTheRecording()
    {
        var writes = _session.Where(e => e.Kind == "write").Select(e => Convert.ToHexString(e.Data)).ToList();

        Assert.Contains(Convert.ToHexString(AncsCommands.GetNotificationAttributes(0)), writes);
        Assert.Contains(Convert.ToHexString(AncsCommands.GetAppDisplayName("com.apple.shortcuts")), writes);
        Assert.Contains(Convert.ToHexString(AncsCommands.PerformNotificationAction(5, positive: false)), writes);
    }

    [Fact]
    public void DataSourceResponsesReassemble()
    {
        var reader = new AncsResponseReader();
        var responses = new List<AncsResponse>();

        // Replay the conversation: every Control Point write announces the response that follows on Data Source.
        foreach (var entry in _session)
        {
            if (entry.Kind == "write" && entry.Data[0] == AncsCommands.GetNotificationAttributesId)
            {
                reader.Expect(AncsCommands.GetNotificationAttributesId, AncsCommands.NotificationAttributes.Count);
            }
            else if (entry.Kind == "write" && entry.Data[0] == AncsCommands.GetAppAttributesId)
            {
                reader.Expect(AncsCommands.GetAppAttributesId, 1);
            }
            else if (entry.Source == "ANCS Data Source" && reader.Add(entry.Data, out var response))
            {
                responses.Add(response!);
            }
        }

        var notifications = responses.Where(r => r.CommandId == 0).ToList();
        Assert.Equal(6, notifications.Count);

        var whatsApp = notifications.First(r => r[AncsAttribute.AppIdentifier] == "net.whatsapp.WhatsApp");
        Assert.Equal("Ayşe", whatsApp[AncsAttribute.Title]);
        Assert.Equal("Akşam görüşelim mi?", whatsApp[AncsAttribute.Message]);
        Assert.Equal("23", whatsApp[AncsAttribute.MessageSize]);
        Assert.Equal("Clear", whatsApp[AncsAttribute.NegativeActionLabel]);
        Assert.Equal(string.Empty, whatsApp[AncsAttribute.PositiveActionLabel]);
        Assert.Contains(notifications, r => r[AncsAttribute.Message] == "Tamam 👍");

        var appNames = responses.Where(r => r.CommandId == 1).ToDictionary(r => r.AppIdentifier!, r => TextCleanup.Clean(r.Values[0]));
        Assert.Equal("WhatsApp", appNames["net.whatsapp.WhatsApp"]);
        Assert.Equal("Shortcuts", appNames["com.apple.shortcuts"]);
    }

    [Fact]
    public void FragmentedResponseCompletesOnlyAtTheEnd()
    {
        var full = _session.First(e => e.Source == "ANCS Data Source" && e.Data[0] == 0 && e.Data.Length > 100).Data;
        var reader = new AncsResponseReader();
        reader.Expect(AncsCommands.GetNotificationAttributesId, AncsCommands.NotificationAttributes.Count);

        // As at a 23-byte ATT MTU: 20-byte fragments.
        AncsResponse? response = null;
        var completedAt = -1;

        for (var offset = 0; offset < full.Length; offset += 20)
        {
            if (reader.Add(full.AsSpan(offset, Math.Min(20, full.Length - offset)), out var complete))
            {
                response = complete;
                completedAt = offset;
            }
        }

        Assert.NotNull(response);
        Assert.True(completedAt + 20 >= full.Length);
        Assert.False(reader.Expecting);
    }

    [Fact]
    public void DateParses()
    {
        var date = AncsCommands.ParseDate("20261015T235931", TimeSpan.FromHours(3));

        Assert.Equal(new DateTimeOffset(2026, 10, 15, 23, 59, 31, TimeSpan.FromHours(3)), date);
        Assert.Null(AncsCommands.ParseDate("garbage", TimeSpan.Zero));
    }

    private static AncsNotificationEvent Parse(byte[] data)
    {
        Assert.True(AncsNotificationEvent.TryParse(data, out var value));
        return value;
    }
}
