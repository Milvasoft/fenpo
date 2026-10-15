using System.Text;
using System.Text.Json;

namespace Fenpo.Spikes.S2;

/// <summary>
/// Thread-safe console output with timestamps.
/// </summary>
internal static class Log
{
    private static readonly Lock _lock = new();

    public static void Info(string area, string message)
    {
        lock (_lock)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{area}] {message}");
        }
    }

    public static void Warn(string area, string message)
    {
        lock (_lock)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{area}] {message}");
            Console.ForegroundColor = previous;
        }
    }
}

/// <summary>
/// Writes every OBEX packet to a JSON Lines file under captures/ (git ignored).
/// </summary>
internal sealed class CaptureLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    public CaptureLog(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        _writer = new StreamWriter(FilePath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public string FilePath { get; }

    public void Write(string kind, string source, ReadOnlySpan<byte> data, string? note = null)
    {
        var entry = new CaptureEntry(DateTimeOffset.Now.ToString("O"), kind, source, Convert.ToHexString(data), note);
        var line = JsonSerializer.Serialize(entry);

        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose() => _writer.Dispose();

    private sealed record CaptureEntry(string T, string Kind, string Source, string Hex, string? Note);
}
