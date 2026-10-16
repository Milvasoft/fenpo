using Fenpo.Core.Messages;
using Fenpo.Core.Notifications;

namespace Fenpo.Core.Tests.Messages;

public sealed class SenderNamesTests
{
    private static readonly DateTimeOffset _at = new(2026, 10, 16, 0, 21, 14, TimeSpan.FromHours(3));

    [Fact]
    public void NotificationTitleNamesTheNumber()
    {
        var names = SenderNames.Learn([Received("+905550000002", "Hey")], [Sms("Ayşe", "Hey", _at.AddSeconds(2))]);

        Assert.Equal("+905550000002", names["Ayşe"]);
    }

    [Fact]
    public void DifferentTextOrTimeDoesNotMatch()
    {
        var names = SenderNames.Learn(
            [Received("+905550000002", "Hey")],
            [Sms("Ayşe", "Başka bir şey", _at), Sms("Mert", "Hey", _at.AddHours(2))]);

        Assert.Empty(names);
    }

    [Fact]
    public void OtherAppsAndNumberTitlesAreIgnored()
    {
        var whatsapp = Sms("Ayşe", "Hey", _at) with { AppId = "net.whatsapp.WhatsApp" };
        var unknown = Sms("+90 555 000 00 02", "Hey", _at);

        Assert.Empty(SenderNames.Learn([Received("+905550000002", "Hey")], [whatsapp, unknown]));
    }

    [Fact]
    public void LongTextsMatchOnTheirStart()
    {
        var text = "Yarın sabah toplantıdan önce sunumun son halini bir kez daha gözden geçirelim mi?";
        var names = SenderNames.Learn([Received("+905550000002", text)], [Sms("Ayşe", text[..60] + "…", _at)]);

        Assert.Single(names);
    }

    private static PhoneMessage Received(string number, string text) => new()
    {
        Handle = "446DE16F42E043F",
        Folder = MessageFolder.Inbox,
        Name = number,
        Address = number,
        Text = text,
        Date = _at,
    };

    private static PhoneNotification Sms(string title, string message, DateTimeOffset date) => new()
    {
        Key = "1:1",
        AppId = SenderNames.MessagesAppId,
        AppName = "Mesajlar",
        Title = title,
        Message = message,
        Date = date,
    };
}
