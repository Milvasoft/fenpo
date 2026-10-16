using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// App icons for iPhone notifications: iTunes Lookup by bundle id (RESEARCH §14), cached as PNG files in the local folder
/// forever, with a negative cache so an unknown bundle is asked once a day at most. The lookup API allows about 20
/// requests a minute, so requests are spaced out.
/// </summary>
public sealed class AppIconService(ILogger<AppIconService> logger)
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim _rateGate = new(1, 1);

    private readonly ILogger<AppIconService> _logger = logger;
    private readonly string _folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "icons");
    private readonly ConcurrentDictionary<string, Task<string?>> _pending = new();

    /// <summary>
    /// Gets the cached icon path or downloads it.
    /// </summary>
    /// <param name="bundleId">App bundle id, e.g. "net.whatsapp.WhatsApp".</param>
    /// <returns>Full path of a PNG, or null when there is none.</returns>
    public Task<string?> GetIconPathAsync(string bundleId)
    {
        if (string.IsNullOrEmpty(bundleId))
        {
            return Task.FromResult<string?>(null);
        }

        var path = PathFor(bundleId);

        if (File.Exists(path))
        {
            return Task.FromResult<string?>(path);
        }

        return _pending.GetOrAdd(bundleId, LoadAsync);
    }

    /// <summary>
    /// Gets the cached icon path without downloading.
    /// </summary>
    /// <param name="bundleId">App bundle id.</param>
    /// <returns>Full path of a PNG, or null.</returns>
    public string? GetCachedIconPath(string bundleId) => !string.IsNullOrEmpty(bundleId) && File.Exists(PathFor(bundleId)) ? PathFor(bundleId) : null;

    private async Task<string?> LoadAsync(string bundleId)
    {
        var path = PathFor(bundleId);
        var missingMarker = path + ".missing";

        try
        {
            if (File.Exists(missingMarker) && DateTime.Now - File.GetLastWriteTime(missingMarker) < TimeSpan.FromDays(1))
            {
                return null;
            }

            await _rateGate.WaitAsync();

            try
            {
                var lookup = $"https://itunes.apple.com/lookup?bundleId={Uri.EscapeDataString(bundleId)}&country=tr";
                using var document = JsonDocument.Parse(await _http.GetStringAsync(lookup));
                var results = document.RootElement.GetProperty("results");

                if (results.GetArrayLength() == 0 || !results[0].TryGetProperty("artworkUrl512", out var artwork))
                {
                    Directory.CreateDirectory(_folder);
                    await File.WriteAllTextAsync(missingMarker, string.Empty);
                    return null;
                }

                var bytes = await _http.GetByteArrayAsync(artwork.GetString());
                Directory.CreateDirectory(_folder);
                await File.WriteAllBytesAsync(path, bytes);
                return path;
            }
            finally
            {
                // Spread lookups: the public API allows about 20 per minute.
                _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => _rateGate.Release(), TaskScheduler.Default);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _logger.LogInformation("No icon for {BundleId}: {Message}", bundleId, exception.Message);
            return null;
        }
        finally
        {
            _pending.TryRemove(bundleId, out _);
        }
    }

    private string PathFor(string bundleId) => Path.Combine(_folder, string.Concat(bundleId.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_')) + ".png");
}
