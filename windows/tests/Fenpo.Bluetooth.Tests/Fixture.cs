using System.Text.Json;

namespace Fenpo.Bluetooth.Tests;

/// <summary>
/// One recorded GATT event from tests/Fenpo.Bluetooth.Tests/Fixtures.
/// </summary>
internal sealed record FixtureEntry(int Ms, string Kind, string Source, byte[] Data)
{
    public static IReadOnlyList<FixtureEntry> Load(string name) => File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Select(e => new FixtureEntry(
                e.TryGetProperty("ms", out var ms) ? ms.GetInt32() : 0,
                e.GetProperty("kind").GetString()!,
                e.GetProperty("source").GetString()!,
                Convert.FromHexString(e.GetProperty("hex").GetString()!)))
            .ToList();
}
