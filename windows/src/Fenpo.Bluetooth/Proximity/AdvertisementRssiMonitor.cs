using Fenpo.Bluetooth.Link;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth.Advertisement;

namespace Fenpo.Bluetooth.Proximity;

/// <summary>
/// Passive advertisement scanning for the paired iPhone's signal strength. Windows resolves the phone's rotating private
/// address to the bonded address, so advertisements can be matched to the bond (S1).
/// </summary>
public sealed class AdvertisementRssiMonitor(IphoneLink link, ILogger<AdvertisementRssiMonitor> logger) : IDisposable
{
    private readonly IphoneLink _link = link;
    private readonly ILogger<AdvertisementRssiMonitor> _logger = logger;
    private BluetoothLEAdvertisementWatcher? _watcher;

    /// <summary>
    /// Raised for every advertisement from the phone, with the time and RSSI in dBm.
    /// </summary>
    public event EventHandler<(DateTimeOffset At, int Rssi)>? Sample;

    /// <summary>
    /// Starts scanning.
    /// </summary>
    public void Start()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        _watcher.Received += (_, args) =>
        {
            if (args.BluetoothAddress == _link.Address && _link.Address != 0)
            {
                Sample?.Invoke(this, (DateTimeOffset.Now, args.RawSignalStrengthInDBm));
            }
        };
        _watcher.Stopped += (_, args) => _logger.LogInformation("Advertisement watcher stopped: {Error}", args.Error);
        _watcher.Start();
        _logger.LogInformation("Advertisement watcher started");
    }

    /// <summary>
    /// Stops scanning.
    /// </summary>
    public void Stop()
    {
        _watcher?.Stop();
        _watcher = null;
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();
}
