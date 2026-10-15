using System.Text;
using Fenpo.Spikes.S1;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

// Usage:
//   AncsSpike [run] [--name <part of device name>]   connect to the paired iPhone, ANCS + AMS + BAS + CTS + DIS + RSSI
//   AncsSpike scan                                    list nearby devices for in-app pairing
//   AncsSpike pair <device id>                        pair from inside the app
Console.OutputEncoding = Encoding.UTF8;

var mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "run";

switch (mode)
{
    case "scan":
        await Pairing.ScanAsync();
        return 0;
    case "pair" when args.Length > 1:
        await Pairing.PairAsync(args[1]);
        return 0;
    case "run":
        return await RunAsync(OptionValue("--name"));
    default:
        Console.WriteLine("Usage: AncsSpike [run [--name <name>] | scan | pair <device id>]");
        return 1;
}

string? OptionValue(string option)
{
    var index = Array.IndexOf(args, option);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static async Task<int> RunAsync(string? namePart)
{
    using var capture = new CaptureLog(Path.Combine(FindSpikeRoot(), "captures"));
    Log.Info("main", $"Capturing raw bytes to {capture.FilePath}");

    var device = await FindPairedDeviceAsync(namePart);

    if (device is null)
    {
        return 1;
    }

    var pairing = device.DeviceInformation.Pairing;
    Log.Info("main", $"Device: {device.Name}, LE address {RssiMonitor.FormatAddress(device.BluetoothAddress)} ({device.BluetoothAddressType}), paired={pairing.IsPaired}, protection={pairing.ProtectionLevel}, status={device.ConnectionStatus}");
    device.NameChanged += (sender, _) => Log.Info("main", $"Name changed: {sender.Name}");

    using var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
    session.MaintainConnection = true;
    session.MaxPduSizeChanged += (sender, _) => Log.Info("main", $"Max PDU size: {sender.MaxPduSize}");
    session.SessionStatusChanged += (_, e) => Log.Info("main", $"GATT session: {e.Status} {e.Error}");

    var link = new IphoneLink(device, capture);
    link.Attach();

    await link.StartAsync(async services =>
    {
        await Discovery.PrintCharacteristicsAsync(services.Values);
        Log.Info("main", $"Max PDU size: {session.MaxPduSize}");
        await Discovery.TryServiceChangedAsync(services, capture);
        await Discovery.ReadDeviceInformationAsync(services, capture);
        await Discovery.ReadCurrentTimeAsync(services, capture);
        await Discovery.ReadBatteryAsync(services, capture);
    });

    var classicAddress = await FindClassicAddressAsync(device.Name);
    using var rssi = new RssiMonitor(device.BluetoothAddress, classicAddress, capture);
    rssi.Start();

    Log.Info("main", "Commands: p play/pause, n next, b previous, + volume up, - volume down, y <uid> positive action, x <uid> negative action, s status, r restart sessions, q quit");

    while (Console.ReadLine() is { } line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            continue;
        }

        try
        {
            await HandleCommandAsync(parts, link, device, rssi);
        }
        catch (ObjectDisposedException)
        {
            Log.Warn("main", "GATT objects were disposed (iPhone reconnected); sessions restart on their own, try again in a few seconds");
        }

        if (parts[0] == "q")
        {
            return 0;
        }
    }

    return 0;
}

static async Task HandleCommandAsync(string[] parts, IphoneLink link, BluetoothLEDevice device, RssiMonitor rssi)
{
    var ancs = link.Ancs;
    var ams = link.Ams;

    switch (parts[0])
    {
        case "q":
            break;
        case "p" when ams is not null:
            await ams.SendAsync(2);
            break;
        case "n" when ams is not null:
            await ams.SendAsync(3);
            break;
        case "b" when ams is not null:
            await ams.SendAsync(4);
            break;
        case "+" when ams is not null:
            await ams.SendAsync(5);
            break;
        case "-" when ams is not null:
            await ams.SendAsync(6);
            break;
        case "y" or "x" when ancs is not null && parts.Length > 1 && uint.TryParse(parts[1], out var uid):
            await ancs.PerformActionAsync(uid, parts[0] == "y");
            break;
        case "s":
            Log.Info("status", $"connection={device.ConnectionStatus}, restarts={link.Restarts}, ancs added={ancs?.Added} removed={ancs?.Removed}, now playing={ams?.NowPlaying}");
            Log.Info("status", rssi.Summary());
            break;
        case "r":
            await link.StartAsync();
            break;
        default:
            Log.Warn("main", $"Unknown command: {string.Join(' ', parts)}");
            break;
    }
}

static async Task<BluetoothLEDevice?> FindPairedDeviceAsync(string? namePart)
{
    var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
    var matches = paired.Where(d => namePart is null || d.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase)).ToList();

    if (matches.Count != 1)
    {
        Log.Warn("main", $"Expected one paired LE device, found {matches.Count}: {string.Join(", ", paired.Select(d => d.Name))}. Use --name.");
        return null;
    }

    var device = await BluetoothLEDevice.FromIdAsync(matches[0].Id);

    if (device is null)
    {
        Log.Warn("main", "BluetoothLEDevice.FromIdAsync returned null (is Bluetooth on?)");
    }

    return device;
}

static async Task<ulong?> FindClassicAddressAsync(string name)
{
    var paired = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
    var match = paired.FirstOrDefault(d => d.Name == name);

    if (match is null)
    {
        return null;
    }

    using var classic = await BluetoothDevice.FromIdAsync(match.Id);
    return classic?.BluetoothAddress;
}

static string FindSpikeRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null && directory.Name != "S1-ancs-ams")
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? Environment.CurrentDirectory;
}
