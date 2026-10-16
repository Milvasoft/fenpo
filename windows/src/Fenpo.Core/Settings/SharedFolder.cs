namespace Fenpo.Core.Settings;

/// <summary>
/// A PC folder the phone can browse.
/// </summary>
/// <param name="Name">Name the phone shows; unique among the shared folders.</param>
/// <param name="Path">Full path on the PC.</param>
/// <param name="ReadOnly">Whether the phone may only read it.</param>
public sealed record SharedFolder(string Name, string Path, bool ReadOnly);
