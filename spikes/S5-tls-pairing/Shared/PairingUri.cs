using System.Buffers.Text;
using System.Web;

namespace Fenpo.Spikes.S5;

/// <summary>
/// fenpo://pair?v=1&amp;id=&lt;pcId&gt;&amp;n=&lt;name&gt;&amp;a=&lt;ip:port,...&gt;&amp;fp=&lt;hex sha256&gt;&amp;t=&lt;base64url token&gt;
/// </summary>
public sealed record PairingUri(int Version, string PcId, string Name, IReadOnlyList<string> Addresses, string Fingerprint, byte[] Token)
{
    public override string ToString() =>
        $"fenpo://pair?v={Version}&id={PcId}&n={Uri.EscapeDataString(Name)}&a={string.Join(",", Addresses)}&fp={Fingerprint}&t={Base64Url.EncodeToString(Token)}";

    public static PairingUri Parse(string text)
    {
        var uri = new Uri(text);

        if (uri.Scheme != "fenpo" || uri.Host != "pair")
        {
            throw new FormatException("Not a fenpo://pair URI");
        }

        var query = HttpUtility.ParseQueryString(uri.Query);
        string Required(string key) => query[key] ?? throw new FormatException($"Missing '{key}'");

        return new PairingUri(
            int.Parse(Required("v")),
            Required("id"),
            Required("n"),
            Required("a").Split(',', StringSplitOptions.RemoveEmptyEntries),
            Required("fp"),
            Base64Url.DecodeFromChars(Required("t")));
    }
}
