using Fenpo.Core.Settings;

namespace Fenpo.Core.Files;

/// <summary>
/// Turns a path the phone sends (files.proto: relative to the shared folders, "/" between parts) into a path on the
/// PC, and refuses anything that would leave a shared folder.
/// </summary>
public static class SharedPaths
{
    /// <summary>
    /// The full path for <paramref name="path"/>, or null when it is not inside one of <paramref name="shared"/>: empty,
    /// "." or ".." parts, backslashes, drive letters, invalid names, an unknown shared folder, or a step below the shared
    /// folder that is a link or junction (<paramref name="isLink"/>).
    /// </summary>
    /// <param name="shared">The shared folders.</param>
    /// <param name="path">The phone's path; its first part is a shared folder's name.</param>
    /// <param name="isLink">Whether an existing path on the PC is a link (reparse point).</param>
    /// <param name="root">The shared folder the path is in.</param>
    /// <returns>The full path, whether or not it exists.</returns>
    public static string? Resolve(IReadOnlyList<SharedFolder> shared, string path, Func<string, bool> isLink, out SharedFolder? root)
    {
        root = null;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || parts.Any(p => p.Trim() is "." or ".." || p.IndexOfAny(['\\', ':']) >= 0 || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return null;
        }

        root = shared.FirstOrDefault(f => string.Equals(f.Name, parts[0], StringComparison.OrdinalIgnoreCase));

        if (root is null)
        {
            return null;
        }

        var basePath = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine([basePath, .. parts[1..]]));

        if (!full.Equals(basePath, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            root = null;
            return null;
        }

        var step = basePath;

        foreach (var part in parts[1..])
        {
            step = Path.Combine(step, part);

            if (isLink(step))
            {
                root = null;
                return null;
            }
        }

        return full;
    }

    /// <summary>
    /// Whether an existing file or folder is a link or junction; false when it does not exist.
    /// </summary>
    /// <param name="path">Full path.</param>
    /// <returns>True for a reparse point.</returns>
    public static bool IsLink(string path) => (File.Exists(path) || Directory.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
