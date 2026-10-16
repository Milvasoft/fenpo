using System.Text;
using System.Xml.Linq;

namespace Fenpo.Bluetooth.Map;

/// <summary>
/// One event of an x-bt/MAP-event-report (NewMessage, MessageShift, SendingSuccess, ...). The iPhone sends MAP 1.0
/// reports: type, handle, folder and message type only (S2).
/// </summary>
/// <param name="Type">Event type.</param>
/// <param name="Handle">Message handle.</param>
/// <param name="Folder">Folder, e.g. "telecom/msg/inbox".</param>
/// <param name="MessageType">Message type, e.g. "SMS_GSM".</param>
public sealed record MapEvent(string Type, string Handle, string Folder, string MessageType)
{
    /// <summary>
    /// Parses an event report.
    /// </summary>
    /// <param name="body">Report bytes (UTF-8 XML, may end with a null).</param>
    /// <returns>The events.</returns>
    public static IReadOnlyList<MapEvent> Parse(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body).TrimEnd('\0').Trim();

        if (text.Length == 0 || XDocument.Parse(text).Root is not { } root)
        {
            return [];
        }

        return [.. root.Elements("event").Select(e => new MapEvent(
            e.Attribute("type")?.Value ?? string.Empty,
            e.Attribute("handle")?.Value ?? string.Empty,
            e.Attribute("folder")?.Value ?? string.Empty,
            e.Attribute("msg_type")?.Value ?? string.Empty))];
    }
}
