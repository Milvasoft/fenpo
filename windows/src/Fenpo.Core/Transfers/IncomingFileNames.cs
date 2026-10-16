namespace Fenpo.Core.Transfers;

/// <summary>
/// Turns a file name chosen on the phone into a safe name in the incoming folder: no path, no characters Windows
/// refuses, no device names, and never over an existing file.
/// </summary>
public static class IncomingFileNames
{
    private const int _maxLength = 200;

    private static readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Makes a name safe to create in a folder.
    /// </summary>
    /// <param name="name">Name from the offer.</param>
    /// <returns>A plain file name.</returns>
    public static string Safe(string name)
    {
        // The offer carries a name only, but a phone could still send "../x" or "C:\x".
        var plain = name.Replace('\\', '/');
        plain = plain[(plain.LastIndexOf('/') + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        var chars = plain.Select(c => c < 32 || invalid.Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim().TrimEnd('.');

        if (safe.Length == 0 || safe.All(c => c == '.'))
        {
            safe = "file";
        }

        var stem = Path.GetFileNameWithoutExtension(safe);

        if (_reserved.Contains(stem))
        {
            safe = "_" + safe;
        }

        if (safe.Length > _maxLength)
        {
            var extension = Path.GetExtension(safe);
            extension = extension.Length > 20 ? string.Empty : extension;
            safe = safe[..(_maxLength - extension.Length)] + extension;
        }

        return safe;
    }

    /// <summary>
    /// Finds a path in the folder that does not exist yet: "name.ext", then "name (2).ext", ...
    /// </summary>
    /// <param name="folder">Incoming folder.</param>
    /// <param name="name">Safe file name.</param>
    /// <param name="taken">Names already chosen in this transfer but not created yet.</param>
    /// <returns>Full path.</returns>
    public static string Unique(string folder, string name, ISet<string>? taken = null)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = name;

        for (var number = 2; File.Exists(Path.Combine(folder, candidate)) || (taken?.Contains(candidate) ?? false); number++)
        {
            candidate = $"{stem} ({number}){extension}";
        }

        taken?.Add(candidate);
        return Path.Combine(folder, candidate);
    }
}
