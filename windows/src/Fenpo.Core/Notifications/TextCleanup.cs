using System.Text;

namespace Fenpo.Core.Notifications;

/// <summary>
/// Cleans notification text from the phone: S1 saw a leading U+200E on WhatsApp's display name and long runs of
/// soft hyphens and spaces at the end of newsletter previews.
/// </summary>
public static class TextCleanup
{
    /// <summary>
    /// Removes bidirectional control marks and soft hyphens, turns line breaks into spaces where asked, and collapses
    /// repeated whitespace.
    /// </summary>
    /// <param name="text">Text as received.</param>
    /// <param name="singleLine">True to replace line breaks with spaces.</param>
    /// <returns>The cleaned text, trimmed.</returns>
    public static string Clean(string? text, bool singleLine = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;

        foreach (var c in text)
        {
            if (IsInvisible(c))
            {
                continue;
            }

            var isNewLine = c is '\n' or '\r';
            var isSpace = char.IsWhiteSpace(c) && (singleLine || !isNewLine);

            if (isSpace)
            {
                if (!previousWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            if (isNewLine)
            {
                // Keep paragraph breaks but drop the space before them.
                if (previousWasSpace && builder.Length > 0)
                {
                    builder.Length--;
                }

                if (c == '\n')
                {
                    builder.Append('\n');
                }

                previousWasSpace = false;
                continue;
            }

            builder.Append(c);
            previousWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool IsInvisible(char c) => c is '­' or '​' or '‌' or '‍' or '‎' or '‏' or '‪' or '‫' or '‬' or '‭' or '‮' or '⁦' or '⁧' or '⁨' or '⁩' or '﻿';
}
