using System.Text.Json;
using Fenpo.Core.Channel;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// BLE channel nonce counters in LocalState. Send counters are reserved in blocks and the block end is saved before
/// any counter from it is used, so a crash can skip counters but never repeat one. The last counter accepted from each
/// phone is saved with every message.
/// </summary>
public sealed class BleCounterStore(ILogger<BleCounterStore> logger) : IBleCounters
{
    private const ulong _block = 1000;

    private readonly ILogger<BleCounterStore> _logger = logger;
    private readonly string _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ble-counters.json");
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ulong> _nextSend = [];
    private Counters? _saved;

    /// <inheritdoc/>
    public ulong NextSend(string deviceId)
    {
        lock (_lock)
        {
            var saved = Load();

            // Start after everything a previous run could have used.
            var next = _nextSend.TryGetValue(deviceId, out var value) ? value : saved.SendReserved.GetValueOrDefault(deviceId) + 1;

            if (next > saved.SendReserved.GetValueOrDefault(deviceId))
            {
                saved.SendReserved[deviceId] = next + _block;
                Save(saved);
            }

            _nextSend[deviceId] = next + 1;
            return next;
        }
    }

    /// <inheritdoc/>
    public bool TryAcceptReceived(string deviceId, ulong counter)
    {
        lock (_lock)
        {
            var saved = Load();

            if (saved.Received.TryGetValue(deviceId, out var last) && counter <= last)
            {
                return false;
            }

            saved.Received[deviceId] = counter;
            Save(saved);
            return true;
        }
    }

    /// <inheritdoc/>
    public void Reset(string deviceId)
    {
        lock (_lock)
        {
            var saved = Load();
            saved.SendReserved.Remove(deviceId);
            saved.Received.Remove(deviceId);
            _nextSend.Remove(deviceId);
            Save(saved);
        }
    }

    private Counters Load()
    {
        if (_saved is not null)
        {
            return _saved;
        }

        try
        {
            _saved = File.Exists(_path) ? JsonSerializer.Deserialize<Counters>(File.ReadAllText(_path)) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // Starting over would reuse nonces; refuse to send until the file is readable again.
            _logger.LogError(exception, "BLE counters could not be read");
            throw new InvalidOperationException("BLE counters are unreadable", exception);
        }

        return _saved ??= new Counters();
    }

    private void Save(Counters counters)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(counters));
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Saving BLE counters failed");
            throw;
        }
    }

    private sealed class Counters
    {
        public Dictionary<string, ulong> SendReserved { get; set; } = [];

        public Dictionary<string, ulong> Received { get; set; } = [];
    }
}
