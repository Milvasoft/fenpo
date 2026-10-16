namespace Fenpo.Core.Messages;

/// <summary>
/// Phone number helpers for matching the same person across folders and for telling numbers from sender names.
/// </summary>
public static class PhoneNumbers
{
    /// <summary>
    /// Gets a value indicating whether the text is a phone number a reply can go to (at least 7 digits, nothing but
    /// digits, spaces, "+", "-", "(", ")" and ".").
    /// </summary>
    /// <param name="text">Address.</param>
    /// <returns>True for a phone number.</returns>
    public static bool IsDialable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = 0;

        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is not (' ' or '+' or '-' or '(' or ')' or '.'))
            {
                return false;
            }
        }

        return digits >= 7;
    }

    /// <summary>
    /// Reduces a number to its last ten digits, so "+90 555 000 00 02", "0555 000 0002" and "5550000002" match.
    /// </summary>
    /// <param name="number">Phone number.</param>
    /// <returns>The comparison key.</returns>
    public static string Normalize(string number)
    {
        var digits = new string([.. number.Where(char.IsAsciiDigit)]);
        return digits.Length > 10 ? digits[^10..] : digits;
    }
}
