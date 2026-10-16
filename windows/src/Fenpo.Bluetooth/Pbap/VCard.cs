using System.Text;
using Fenpo.Core.Messages;

namespace Fenpo.Bluetooth.Pbap;

/// <summary>
/// One contact from a phone book: display name and phone numbers.
/// </summary>
/// <param name="Name">Formatted name (FN), or the N parts when FN is empty.</param>
/// <param name="Numbers">Phone numbers as the phone stores them.</param>
public sealed record PhoneContact(string Name, IReadOnlyList<string> Numbers);

/// <summary>
/// Reads the FN, N and TEL properties of vCards (2.1 and 3.0): unfolds continuation lines, ignores groups
/// ("item1.TEL") and parameters, decodes quoted-printable values.
/// </summary>
public static class VCard
{
    /// <summary>
    /// Parses a phone book.
    /// </summary>
    /// <param name="text">One or more vCards.</param>
    /// <returns>Contacts that have a name and at least one number.</returns>
    public static IReadOnlyList<PhoneContact> Parse(string text)
    {
        var contacts = new List<PhoneContact>();
        string? formatted = null, structured = null;
        List<string>? numbers = null;

        foreach (var line in Unfold(text))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0)
            {
                continue;
            }

            var property = line[..colon];
            var parameters = property.Split(';');
            var key = parameters[0];
            var dot = key.LastIndexOf('.');
            key = (dot >= 0 ? key[(dot + 1)..] : key).ToUpperInvariant();
            var value = Decode(line[(colon + 1)..], parameters);

            switch (key)
            {
                case "BEGIN" when value.Equals("VCARD", StringComparison.OrdinalIgnoreCase):
                    formatted = structured = null;
                    numbers = [];
                    break;
                case "END" when value.Equals("VCARD", StringComparison.OrdinalIgnoreCase) && numbers is not null:
                    var name = !string.IsNullOrWhiteSpace(formatted) ? formatted.Trim() : structured ?? string.Empty;

                    if (name.Length > 0 && numbers.Count > 0)
                    {
                        contacts.Add(new PhoneContact(name, numbers));
                    }

                    numbers = null;
                    break;
                case "FN":
                    formatted = value;
                    break;
                case "N":
                    // N is "family;given;additional;prefix;suffix"; shown as "given family".
                    var parts = value.Split(';');
                    structured = string.Join(' ', new[] { parts.ElementAtOrDefault(3), parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2), parts[0], parts.ElementAtOrDefault(4) }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
                    break;
                case "TEL" when numbers is not null && value.Trim().Length > 0:
                    numbers.Add(value.Trim());
                    break;
            }
        }

        return contacts;
    }

    /// <summary>
    /// Builds the number to name map for message and call names; the first contact wins a shared number.
    /// </summary>
    /// <param name="contacts">Contacts.</param>
    /// <returns>Names keyed by normalized number.</returns>
    public static IReadOnlyDictionary<string, string> NamesByNumber(IEnumerable<PhoneContact> contacts)
    {
        var names = new Dictionary<string, string>();

        foreach (var contact in contacts)
        {
            foreach (var number in contact.Numbers.Where(PhoneNumbers.IsDialable))
            {
                names.TryAdd(PhoneNumbers.Normalize(number), contact.Name);
            }
        }

        return names;
    }

    private static IEnumerable<string> Unfold(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            // vCard 3.0 folds with a leading space or tab; 2.1 quoted-printable ends a line with "=".
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && current.Length > 0)
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }

            if (current.Length > 0 && current[^1] == '=' && current.ToString().Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
            {
                current.Length--;
                current.Append(line);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }

            current.Clear().Append(line);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string Decode(string value, string[] parameters)
    {
        if (!parameters.Any(p => p.Equals("ENCODING=QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase) || p.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase)))
        {
            // vCard 3.0 escapes commas and semicolons inside values.
            return value.Replace("\\,", ",", StringComparison.Ordinal).Replace("\\n", " ", StringComparison.OrdinalIgnoreCase);
        }

        var bytes = new List<byte>();

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }
}
