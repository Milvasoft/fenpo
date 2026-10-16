using System.Text;
using Fenpo.Core.Messages;

namespace Fenpo.Bluetooth.Map;

/// <summary>
/// bMessage (MAP 1.4, section 3.1.3): reading a message and building an outgoing SMS. Accepts parameterized property
/// names ("FN;CHARSET=UTF-8") and the iPhone's non-standard lines (NOTIFICATION, LANGUAGE).
/// </summary>
public static class BMessage
{
    /// <summary>
    /// Reads the originator and the text of a bMessage.
    /// </summary>
    /// <param name="text">bMessage.</param>
    /// <returns>The content; empty fields for parts that are missing.</returns>
    public static MessageContent Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string name = string.Empty, address = string.Empty;
        var depth = new Stack<string>();
        var body = new StringBuilder();
        var inMessage = false;
        var originatorDone = false;

        foreach (var line in lines)
        {
            if (inMessage)
            {
                if (line == "END:MSG")
                {
                    inMessage = false;
                    continue;
                }

                body.Append(body.Length > 0 ? "\n" : string.Empty).Append(line);
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0)
            {
                continue;
            }

            var property = line[..colon];
            var semicolon = property.IndexOf(';', StringComparison.Ordinal);
            var key = (semicolon < 0 ? property : property[..semicolon]).ToUpperInvariant();
            var value = line[(colon + 1)..];

            switch (key)
            {
                case "BEGIN" when value == "MSG":
                    inMessage = true;
                    break;
                case "BEGIN":
                    depth.Push(value);
                    break;
                case "END":
                    if (depth.TryPop(out var ended) && ended == "VCARD" && depth.TryPeek(out var parent) && parent == "BMSG")
                    {
                        // The vCard directly under BMSG is the originator; the one inside BENV is the recipient.
                        originatorDone = true;
                    }

                    break;
                case "FN" when !originatorDone && IsOriginator(depth):
                    name = value.Trim();
                    break;
                case "N" when !originatorDone && IsOriginator(depth) && name.Length == 0:
                    name = value.Replace(';', ' ').Trim();
                    break;
                case "TEL" or "EMAIL" when !originatorDone && IsOriginator(depth) && address.Length == 0:
                    address = value.Trim();
                    break;
            }
        }

        return new MessageContent(name, address, body.ToString());
    }

    /// <summary>
    /// Builds a bMessage for an outgoing SMS to the outbox.
    /// </summary>
    /// <param name="number">Recipient number.</param>
    /// <param name="text">Text.</param>
    /// <returns>The bMessage.</returns>
    public static string BuildSms(string number, string text)
    {
        // LENGTH counts from "BEGIN:MSG" through "END:MSG" including their line breaks.
        var content = $"BEGIN:MSG\r\n{text.ReplaceLineEndings("\r\n")}\r\nEND:MSG\r\n";
        var length = Encoding.UTF8.GetByteCount(content);

        return "BEGIN:BMSG\r\n"
            + "VERSION:1.0\r\n"
            + "STATUS:READ\r\n"
            + "TYPE:SMS_GSM\r\n"
            + "FOLDER:telecom/msg/outbox\r\n"
            + "BEGIN:BENV\r\n"
            + "BEGIN:VCARD\r\n"
            + "VERSION:2.1\r\n"
            + "N:\r\n"
            + $"TEL:{number}\r\n"
            + "END:VCARD\r\n"
            + "BEGIN:BBODY\r\n"
            + "CHARSET:UTF-8\r\n"
            + $"LENGTH:{length}\r\n"
            + content
            + "END:BBODY\r\n"
            + "END:BENV\r\n"
            + "END:BMSG\r\n";
    }

    private static bool IsOriginator(Stack<string> depth) => depth.Count == 2 && depth.Peek() == "VCARD";
}
