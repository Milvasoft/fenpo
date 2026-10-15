using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace Fenpo.Spikes.S1;

/// <summary>
/// Looks for a signal strength source for the paired iPhone. A connected GATT link exposes no RSSI in WinRT,
/// so this tries two other paths at once:
/// 1. Association endpoint watchers on the LE and classic addresses (System.Devices.Aep.SignalStrength).
/// 2. A passive advertisement watcher: does the iPhone advertise with the bonded address (Windows resolving
///    its private address), or only with rotating addresses that cannot be tied to the bond?
/// </summary>
internal sealed class RssiMonitor(ulong leAddress, ulong? classicAddress, CaptureLog capture) : IDisposable
{
    private const string _signalStrength = "System.Devices.Aep.SignalStrength";
    private const string _isConnected = "System.Devices.Aep.IsConnected";
    private const string _isPresent = "System.Devices.Aep.IsPresent";
    private const ushort _appleCompanyId = 0x004C;

    private readonly ulong _leAddress = leAddress;
    private readonly ulong? _classicAddress = classicAddress;
    private readonly CaptureLog _capture = capture;
    private readonly List<DeviceWatcher> _watchers = [];
    private readonly BluetoothLEAdvertisementWatcher _advertisements = new() { ScanningMode = BluetoothLEScanningMode.Passive };
    private readonly HashSet<ulong> _appleAddresses = [];
    private readonly Dictionary<string, DateTimeOffset> _lastPrinted = [];
    private readonly Lock _lock = new();
    private int _bondedAdvertisements;
    private int _appleAdvertisements;
    private short? _lastBondedRssi;
    private int? _lastLeAep;
    private int? _lastClassicAep;

    public static string FormatAddress(ulong address) => string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("x2")));

    public void Start()
    {
        StartAepWatcher("le", _leAddress);

        if (_classicAddress is { } classic)
        {
            StartAepWatcher("classic", classic);
        }

        _advertisements.Received += OnAdvertisement;
        _advertisements.Stopped += (_, args) => Log.Warn("rssi", $"Advertisement watcher stopped: {args.Error}");
        _advertisements.Start();
        Log.Info("rssi", $"Watching advertisements and association endpoints (LE {FormatAddress(_leAddress)}{(_classicAddress is { } c ? $", classic {FormatAddress(c)}" : string.Empty)})");
    }

    public string Summary()
    {
        lock (_lock)
        {
            return $"aep-le={_lastLeAep?.ToString() ?? "-"} dBm, aep-classic={_lastClassicAep?.ToString() ?? "-"} dBm, "
                + $"adv-bonded={_lastBondedRssi?.ToString() ?? "-"} dBm ({_bondedAdvertisements} seen), "
                + $"apple adverts={_appleAdvertisements} from {_appleAddresses.Count} addresses";
        }
    }

    public void Dispose()
    {
        _advertisements.Stop();

        foreach (var watcher in _watchers)
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
    }

    private void StartAepWatcher(string label, ulong address)
    {
        var filter = $"System.Devices.Aep.DeviceAddress:=\"{FormatAddress(address)}\"";
        var watcher = DeviceInformation.CreateWatcher(filter, [_signalStrength, _isConnected, _isPresent], DeviceInformationKind.AssociationEndpoint);

        watcher.Added += (_, info) => OnAepProperties(label, info.Properties);
        watcher.Updated += (_, update) => OnAepProperties(label, update.Properties);
        watcher.Removed += (_, _) => Log.Info("rssi", $"aep-{label} removed");
        watcher.EnumerationCompleted += (_, _) => Log.Info("rssi", $"aep-{label} enumeration completed");
        watcher.Stopped += (_, _) => Log.Warn("rssi", $"aep-{label} watcher stopped");
        watcher.Start();
        _watchers.Add(watcher);
    }

    private void OnAepProperties(string label, IReadOnlyDictionary<string, object> properties)
    {
        if (!properties.TryGetValue(_signalStrength, out var value) || value is null)
        {
            return;
        }

        var rssi = Convert.ToInt32(value);
        properties.TryGetValue(_isConnected, out var connected);
        properties.TryGetValue(_isPresent, out var present);

        lock (_lock)
        {
            if (label == "le")
            {
                _lastLeAep = rssi;
            }
            else
            {
                _lastClassicAep = rssi;
            }
        }

        _capture.Write("rssi", $"aep-{label}", [], $"{rssi} dBm connected={connected} present={present}");
        PrintThrottled($"aep-{label}", $"aep-{label} {rssi} dBm connected={connected} present={present}");
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var isApple = args.Advertisement.ManufacturerData.Any(m => m.CompanyId == _appleCompanyId);

        if (args.BluetoothAddress == _leAddress)
        {
            lock (_lock)
            {
                _bondedAdvertisements++;
                _lastBondedRssi = args.RawSignalStrengthInDBm;
            }

            _capture.Write("rssi", "adv-bonded", [], $"{args.RawSignalStrengthInDBm} dBm type={args.AdvertisementType} addressType={args.BluetoothAddressType}");
            PrintThrottled("adv-bonded", $"adv-bonded {args.RawSignalStrengthInDBm} dBm type={args.AdvertisementType} addressType={args.BluetoothAddressType}");
        }
        else if (isApple)
        {
            lock (_lock)
            {
                _appleAdvertisements++;
                _appleAddresses.Add(args.BluetoothAddress);
            }
        }
    }

    private void PrintThrottled(string key, string message)
    {
        lock (_lock)
        {
            // Every value goes to the capture file; the console shows one line per source every 30 seconds.
            if (_lastPrinted.TryGetValue(key, out var last) && DateTimeOffset.Now - last < TimeSpan.FromSeconds(30))
            {
                return;
            }

            _lastPrinted[key] = DateTimeOffset.Now;
        }

        Log.Info("rssi", message);
    }
}
