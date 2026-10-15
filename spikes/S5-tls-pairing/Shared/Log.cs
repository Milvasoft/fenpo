namespace Fenpo.Spikes.S5;

/// <summary>
/// Thread-safe console output with timestamps.
/// </summary>
public static class Log
{
    private static readonly Lock _lock = new();

    public static void Info(string area, string message)
    {
        lock (_lock)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{area}] {message}");
        }
    }
}
