using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Fenpo.Core.Messages;

namespace Fenpo.Bluetooth.Map;

/// <summary>
/// Parses x-bt/MAP-msg-listing. The iPhone sends MAP 1.0 listings and puts the contact's name in sender_addressing (S2).
/// </summary>
public static class MapListing
{
    /// <summary>
    /// Parses a listing.
    /// </summary>
    /// <param name="body">Listing bytes (UTF-8 XML, may end with a null).</param>
    /// <param name="folder">Folder the listing came from.</param>
    /// <param name="timeZone">Time zone of the phone's local datetimes (the PC's, by default).</param>
    /// <returns>Messages, newest first.</returns>
    public static IReadOnlyList<PhoneMessage> Parse(byte[] body, MessageFolder folder, TimeZoneInfo? timeZone = null)
    {
        var text = Encoding.UTF8.GetString(body).TrimEnd('\0').Trim();

        if (text.Length == 0)
        {
            return [];
        }

        var zone = timeZone ?? TimeZoneInfo.Local;
        var root = XDocument.Parse(text).Root;

        if (root is null)
        {
            return [];
        }

        return [.. root.Elements("msg")
            .Select(element => From(element, folder, zone))
            .OfType<PhoneMessage>()
            .OrderByDescending(m => m.Date)];
    }

    /// <summary>
    /// Parses a MAP datetime ("20261016T001145", optionally with a UTC offset "+0300" or "Z").
    /// </summary>
    /// <param name="value">Datetime text.</param>
    /// <param name="zone">Zone for values without an offset.</param>
    /// <returns>The time, or null when it cannot be read.</returns>
    public static DateTimeOffset? ParseDate(string value, TimeZoneInfo zone)
    {
        if (value.Length < 15 || !DateTime.TryParseExact(value[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var suffix = value[15..];

        if (suffix == "Z")
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }

        if (suffix.Length == 5 && int.TryParse(suffix[1..3], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) && int.TryParse(suffix[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            var offset = new TimeSpan(hours, minutes, 0);
            return new DateTimeOffset(local, suffix[0] == '-' ? -offset : offset);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static PhoneMessage? From(XElement element, MessageFolder folder, TimeZoneInfo zone)
    {
        var handle = Attribute(element, "handle");

        if (handle.Length == 0)
        {
            return null;
        }

        var incoming = folder == MessageFolder.Inbox;
        var name = Attribute(element, incoming ? "sender_name" : "recipient_name");
        var address = Attribute(element, incoming ? "sender_addressing" : "recipient_addressing");

        return new PhoneMessage
        {
            Handle = handle,
            Folder = folder,
            Name = name.Length > 0 ? name : address,
            Address = address,
            Text = Attribute(element, "subject"),
            Date = ParseDate(Attribute(element, "datetime"), zone) ?? DateTimeOffset.MinValue,
            IsRead = !incoming || Attribute(element, "read") == "yes",
        };
    }

    private static string Attribute(XElement element, string name) => element.Attribute(name)?.Value.Trim() ?? string.Empty;
}
