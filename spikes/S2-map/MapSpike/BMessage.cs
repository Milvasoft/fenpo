using System.Text;

namespace Fenpo.Spikes.S2;

/// <summary>
/// Just enough bMessage (MAP 1.x, section 3.1.3) to read a message body and build an outgoing SMS.
/// </summary>
internal static class BMessage
{
    public static string BuildOutgoing(string number, string text)
    {
        // LENGTH covers "BEGIN:MSG\r\n" through "END:MSG\r\n".
        var content = $"BEGIN:MSG\r\n{text}\r\nEND:MSG\r\n";
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

    public static string Summarize(string bmessage)
    {
        string Field(string name)
        {
            var line = bmessage.Split("\r\n").FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.Ordinal));
            return line?[(name.Length + 1)..] ?? string.Empty;
        }

        var start = bmessage.IndexOf("BEGIN:MSG\r\n", StringComparison.Ordinal);
        var end = bmessage.LastIndexOf("\r\nEND:MSG", StringComparison.Ordinal);
        var body = start >= 0 && end > start ? bmessage[(start + 11)..end] : "(no body)";
        return $"type={Field("TYPE")} status={Field("STATUS")} folder={Field("FOLDER")} tel={Field("TEL")} name={Field("FN")} charset={Field("CHARSET")} length={Field("LENGTH")} body=\"{body.ReplaceLineEndings(" / ")}\"";
    }
}
