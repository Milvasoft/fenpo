using System.Text;

namespace Fenpo.Core.Pairing;

/// <summary>
/// The QR code's content: <c>fenpo://pair?v=1&amp;id=&amp;n=&amp;a=&amp;fp=&amp;t=</c> (protocol/fenpo/v1/pairing.proto).
/// </summary>
/// <param name="PcId">PC id.</param>
/// <param name="Name">PC name shown on the phone.</param>
/// <param name="Addresses">Endpoints of the TLS server, "ip:port".</param>
/// <param name="Fingerprint">SHA-256 of the PC certificate (DER), 32 bytes.</param>
/// <param name="Token">One-time token, 16 bytes.</param>
public sealed record PairingUri(string PcId, string Name, IReadOnlyList<string> Addresses, byte[] Fingerprint, byte[] Token)
{
    /// <summary>
    /// Gets the URI text for the QR code.
    /// </summary>
    /// <returns>The URI.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder("fenpo://pair?v=1");
        builder.Append("&id=").Append(Uri.EscapeDataString(PcId));
        builder.Append("&n=").Append(Uri.EscapeDataString(Name));
        builder.Append("&a=").Append(string.Join(',', Addresses));
        builder.Append("&fp=").Append(Convert.ToHexStringLower(Fingerprint));
        builder.Append("&t=").Append(Convert.ToBase64String(Token).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        return builder.ToString();
    }

    /// <summary>
    /// Reads a pairing URI.
    /// </summary>
    /// <param name="text">URI text.</param>
    /// <param name="uri">The parsed URI.</param>
    /// <returns>True when it is a version 1 Fenpo pairing URI with every field.</returns>
    public static bool TryParse(string text, out PairingUri? uri)
    {
        uri = null;

        if (!text.StartsWith("fenpo://pair?", StringComparison.Ordinal))
        {
            return false;
        }

        var fields = text["fenpo://pair?".Length..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1]));

        if (fields.GetValueOrDefault("v") != "1" || !fields.TryGetValue("id", out var id) || !fields.TryGetValue("fp", out var fingerprint) || !fields.TryGetValue("t", out var token))
        {
            return false;
        }

        try
        {
            var padded = token.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            uri = new PairingUri(
                id,
                fields.GetValueOrDefault("n") ?? string.Empty,
                [.. (fields.GetValueOrDefault("a") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)],
                Convert.FromHexString(fingerprint),
                Convert.FromBase64String(padded));
            return uri.Fingerprint.Length == 32 && uri.Token.Length == 16;
        }
        catch (FormatException)
        {
            uri = null;
            return false;
        }
    }
}
