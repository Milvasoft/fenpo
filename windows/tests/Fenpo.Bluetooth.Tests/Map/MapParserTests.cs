using System.Text;
using Fenpo.Bluetooth.Map;
using Fenpo.Core.Messages;

namespace Fenpo.Bluetooth.Tests.Map;

public sealed class MapParserTests
{
    private static readonly TimeZoneInfo _istanbul = TimeZoneInfo.CreateCustomTimeZone("TRT", TimeSpan.FromHours(3), "TRT", "TRT");

    [Fact]
    public void InboxListingParses()
    {
        var messages = MapListing.Parse(File.ReadAllBytes(FixturePath("msg-listing-inbox.xml")), MessageFolder.Inbox, _istanbul);

        Assert.Equal(10, messages.Count);
        var newest = messages[0];
        Assert.Equal("2B5F1C9AE1FDE26", newest.Handle);
        Assert.Equal("Ayşe", newest.Name);
        Assert.Equal("Kargonuz yola çıktı, takip no 1234567890.", newest.Text);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 0, 11, 45, TimeSpan.FromHours(3)), newest.Date);
        Assert.False(newest.IsRead);
        Assert.False(newest.IsOutgoing);
        Assert.True(messages[1].IsRead);
        Assert.True(messages.Zip(messages.Skip(1)).All(pair => pair.First.Date >= pair.Second.Date));
    }

    [Theory]
    [InlineData("20261016T001145Z", 0)]
    [InlineData("20261016T001145+0300", 3)]
    [InlineData("20261016T001145-0130", -1.5)]
    public void DatesWithOffsetsParse(string value, double offsetHours)
    {
        var date = MapListing.ParseDate(value, _istanbul);

        Assert.Equal(TimeSpan.FromHours(offsetHours), date?.Offset);
        Assert.Equal(new DateTime(2026, 10, 16, 0, 11, 45), date?.DateTime);
    }

    [Fact]
    public void IncomingBMessageFromContactParses()
    {
        var content = BMessage.Parse(File.ReadAllText(FixturePath("bmessage-incoming-contact.txt")));

        Assert.Equal("Ayşe", content.Name);
        Assert.Equal("+905550000002", content.Address);
        Assert.Equal("Selam 👋", content.Text);
    }

    [Fact]
    public void IncomingBMessageFromShortCodeParses()
    {
        var content = BMessage.Parse(File.ReadAllText(FixturePath("bmessage-incoming-shortcode.txt")));

        Assert.NotEmpty(content.Text);
        Assert.NotEmpty(content.Name + content.Address);
        Assert.DoesNotContain("END:", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void OutgoingSmsMatchesTheOneThePhoneAccepted()
    {
        var expected = File.ReadAllText(FixturePath("bmessage-outgoing.txt")).ReplaceLineEndings("\r\n");

        Assert.Equal(expected, BMessage.BuildSms("+905550000002", "Fenpo deneme"));
    }

    [Fact]
    public void LengthCountsUtf8Bytes()
    {
        var message = BMessage.BuildSms("+905550000002", "Görüşürüz 👋");

        Assert.Contains($"LENGTH:{Encoding.UTF8.GetByteCount("BEGIN:MSG\r\nGörüşürüz 👋\r\nEND:MSG\r\n")}\r\n", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("event-report-NewMessage.xml", "NewMessage", "446DE16F42E043F", "telecom/msg/inbox")]
    [InlineData("event-report-MessageShift.xml", "MessageShift", "6099C3D50857492", "telecom/msg/sent")]
    [InlineData("event-report-SendingSuccess.xml", "SendingSuccess", "6099C3D50857492", "telecom/msg/sent")]
    public void EventReportsParse(string file, string type, string handle, string folder)
    {
        var mapEvent = Assert.Single(MapEvent.Parse(File.ReadAllBytes(FixturePath(file))));

        Assert.Equal(type, mapEvent.Type);
        Assert.Equal(handle, mapEvent.Handle);
        Assert.Equal(folder, mapEvent.Folder);
        Assert.Equal("SMS_GSM", mapEvent.MessageType);
    }

    [Fact]
    public void ParametersAreFoundByTag()
    {
        var data = MapParameters.Build((MapParameters.ListingSize, MapParameters.U16(258)), (MapParameters.Charset, MapParameters.U8(1)));

        Assert.Equal([0x01, 0x02], MapParameters.Find(data, MapParameters.ListingSize));
        Assert.Equal([0x01], MapParameters.Find(data, MapParameters.Charset));
        Assert.Null(MapParameters.Find(data, MapParameters.MseTime));
        Assert.Null(MapParameters.Find([0x12, 0x05, 0x00], MapParameters.ListingSize));
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Map", name);
}
