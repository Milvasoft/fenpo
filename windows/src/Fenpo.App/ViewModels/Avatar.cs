using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Letter avatars for people and apps: the first letter on a color picked from the name, the same on every screen.
/// </summary>
public static class Avatar
{
    // Avatar fills from the design (people and app tiles); white text reads on all of them in both themes.
    private static readonly Windows.UI.Color[] _palette =
    [
        Windows.UI.Color.FromArgb(0xFF, 0x25, 0xA3, 0x5A),
        Windows.UI.Color.FromArgb(0xFF, 0x3B, 0x7B, 0xF6),
        Windows.UI.Color.FromArgb(0xFF, 0xF2, 0x99, 0x4A),
        Windows.UI.Color.FromArgb(0xFF, 0x4E, 0x54, 0xC8),
        Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x57, 0x7E),
        Windows.UI.Color.FromArgb(0xFF, 0x2F, 0x8F, 0x6B),
        Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x90, 0xAC),
    ];

    /// <summary>
    /// Gets the letter for a name, or "?" for an empty one.
    /// </summary>
    /// <param name="text">Name.</param>
    /// <returns>One upper-case letter.</returns>
    public static string Initial(string text) => string.IsNullOrWhiteSpace(text) ? "?" : char.ToUpper(text.Trim()[0], CultureInfo.CurrentCulture).ToString();

    /// <summary>
    /// Gets the fill for a name; the same name always gets the same color.
    /// </summary>
    /// <param name="seed">Name or app id.</param>
    /// <returns>The brush.</returns>
    public static Brush Fill(string seed) => new SolidColorBrush(string.IsNullOrEmpty(seed) ? Colors.Gray : _palette[(int)((uint)StableHash(seed) % (uint)_palette.Length)]);

    private static int StableHash(string text)
    {
        var hash = 17;

        foreach (var c in text)
        {
            hash = unchecked((hash * 31) + c);
        }

        return hash;
    }
}
