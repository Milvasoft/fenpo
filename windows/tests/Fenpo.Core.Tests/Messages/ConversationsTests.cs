using Fenpo.Core.Messages;

namespace Fenpo.Core.Tests.Messages;

public sealed class ConversationsTests
{
    private static readonly DateTimeOffset _start = new(2026, 10, 16, 9, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void ReceivedByNameAndSentByNumberJoinOnceTheNumberIsKnown()
    {
        PhoneMessage[] messages =
        [
            Received("1", "Ayşe", "Ayşe", "Selam", 0),
            Sent("2", "+90 555 000 00 02", "Merhaba", 5),
            Received("3", "Kargo", "KARGO", "Paketiniz yolda", 2),
        ];

        var conversations = Conversations.Build(messages, new Dictionary<string, string> { ["Ayşe"] = "05550000002" });

        Assert.Equal(2, conversations.Count);
        var ayse = conversations[0];
        Assert.Equal("Ayşe", ayse.Name);
        Assert.Equal(["Selam", "Merhaba"], ayse.Messages.Select(m => m.Text));
        Assert.True(ayse.CanReply);
        Assert.Equal("+90 555 000 00 02", ayse.Address);
        Assert.False(conversations[1].CanReply);
    }

    [Fact]
    public void WithoutTheNumberTheyStaySeparate()
    {
        PhoneMessage[] messages = [Received("1", "Ayşe", "Ayşe", "Selam", 0), Sent("2", "+905550000002", "Merhaba", 5)];

        var conversations = Conversations.Build(messages, new Dictionary<string, string>());

        Assert.Equal(2, conversations.Count);
        Assert.Equal("+905550000002", conversations[0].Name);
        Assert.False(conversations[1].CanReply);
    }

    [Fact]
    public void UnreadCountsOnlyReceivedUnread()
    {
        PhoneMessage[] messages =
        [
            Received("1", "Mert", "+905551112233", "Akşam?", 0) with { IsRead = false },
            Received("2", "Mert", "+905551112233", "Maç var", 1) with { IsRead = false },
            Sent("3", "+905551112233", "Geliyorum", 2),
        ];

        var conversation = Assert.Single(Conversations.Build(messages, new Dictionary<string, string>()));

        Assert.Equal(2, conversation.UnreadCount);
        Assert.Equal("Geliyorum", conversation.Last.Text);
        Assert.Equal("Mert", conversation.Name);
    }

    [Theory]
    [InlineData("+90 555 000 00 02", true)]
    [InlineData("(0555) 000-0002", true)]
    [InlineData("4455", false)]
    [InlineData("KARGO", false)]
    [InlineData("Ayşe", false)]
    [InlineData("", false)]
    public void TellsNumbersFromNames(string text, bool dialable) => Assert.Equal(dialable, PhoneNumbers.IsDialable(text));

    [Fact]
    public void NormalizesToLastTenDigits()
    {
        Assert.Equal("5550000002", PhoneNumbers.Normalize("+90 555 000 00 02"));
        Assert.Equal("5550000002", PhoneNumbers.Normalize("0555 000 0002"));
        Assert.Equal("4455", PhoneNumbers.Normalize("4455"));
    }

    private static PhoneMessage Received(string handle, string name, string address, string text, int minutes) => new()
    {
        Handle = handle,
        Folder = MessageFolder.Inbox,
        Name = name,
        Address = address,
        Text = text,
        Date = _start.AddMinutes(minutes),
        IsRead = true,
    };

    private static PhoneMessage Sent(string handle, string address, string text, int minutes) => new()
    {
        Handle = handle,
        Folder = MessageFolder.Sent,
        Name = address,
        Address = address,
        Text = text,
        Date = _start.AddMinutes(minutes),
        IsRead = true,
    };
}
