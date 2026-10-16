using System.Security.Cryptography;
using System.Text.Json;
using Fenpo.Core.Channel;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// Phones paired with the Fenpo app and their BLE channel keys, in LocalState. Keys are protected with DPAPI for the
/// current user, so the file is useless on another account or machine. Filled by Wi‑Fi pairing (S5).
/// </summary>
public sealed class PairedAppDeviceStore(ILogger<PairedAppDeviceStore> logger) : IPairedAppDevices
{
    private static readonly byte[] _entropy = "fenpo-ble-channel-key"u8.ToArray();

    private readonly ILogger<PairedAppDeviceStore> _logger = logger;
    private readonly string _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "paired-apps.json");
    private readonly Lock _lock = new();
    private List<PairedAppDevice>? _devices;

    /// <inheritdoc/>
    public IReadOnlyList<PairedAppDevice> All
    {
        get
        {
            lock (_lock)
            {
                return [.. Load()];
            }
        }
    }

    /// <inheritdoc/>
    public void Save(PairedAppDevice device)
    {
        lock (_lock)
        {
            var devices = Load();
            devices.RemoveAll(d => d.DeviceId == device.DeviceId);
            devices.Add(device);
            Write(devices);
        }
    }

    /// <inheritdoc/>
    public void Remove(string deviceId)
    {
        lock (_lock)
        {
            var devices = Load();

            if (devices.RemoveAll(d => d.DeviceId == deviceId) > 0)
            {
                Write(devices);
            }
        }
    }

    private List<PairedAppDevice> Load()
    {
        if (_devices is not null)
        {
            return _devices;
        }

        _devices = [];

        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<List<StoredDevice>>(File.ReadAllText(_path)) is { } stored)
            {
                foreach (var device in stored)
                {
                    _devices.Add(new PairedAppDevice(device.DeviceId, device.Name, ProtectedData.Unprotect(Convert.FromBase64String(device.ProtectedKey), _entropy, DataProtectionScope.CurrentUser), device.CertificateSha256 ?? string.Empty, device.Platform));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or CryptographicException or FormatException)
        {
            _logger.LogWarning(exception, "Paired app devices could not be read; the phones have to pair again");
            _devices = [];
        }

        return _devices;
    }

    private void Write(List<PairedAppDevice> devices)
    {
        var stored = devices.Select(d => new StoredDevice(d.DeviceId, d.Name, Convert.ToBase64String(ProtectedData.Protect(d.BleKey, _entropy, DataProtectionScope.CurrentUser)), d.CertificateSha256, d.Platform)).ToList();

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(stored));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Saving paired app devices failed");
        }
    }

    private sealed record StoredDevice(string DeviceId, string Name, string ProtectedKey, string? CertificateSha256, Protocol.V1.DevicePlatform Platform = Protocol.V1.DevicePlatform.Unspecified);
}
