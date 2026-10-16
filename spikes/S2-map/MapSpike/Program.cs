using System.Text;
using Fenpo.Spikes.S2;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

// Usage: MapSpike [--name <part of device name>]
// Connects to the paired iPhone's MAP server, lists folders and the inbox, registers for notifications
// (the iPhone then connects to our MNS server) and offers commands to read, mark and send messages.
Console.OutputEncoding = Encoding.UTF8;

var nameIndex = Array.IndexOf(args, "--name");
var namePart = nameIndex >= 0 && nameIndex + 1 < args.Length ? args[nameIndex + 1] : null;

using var capture = new CaptureLog(Path.Combine(FindSpikeRoot(), "captures"));
Log.Info("main", $"Capturing raw bytes to {capture.FilePath}");

var paired = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
var matches = paired.Where(d => namePart is null ? d.Name.Contains("iphone", StringComparison.OrdinalIgnoreCase) : d.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase)).ToList();

if (matches.Count != 1)
{
    Log.Warn("main", $"Expected one paired classic device, found {matches.Count}: {string.Join(", ", paired.Select(d => d.Name))}. Use --name.");
    return 1;
}

using var device = await BluetoothDevice.FromIdAsync(matches[0].Id);
Log.Info("main", $"Device: {device.Name}, connection {device.ConnectionStatus}");
device.ConnectionStatusChanged += (sender, _) => Log.Info("main", $"Connection status: {sender.ConnectionStatus}");

var service = await MasClient.FindServiceAsync(device);

if (service is null)
{
    Log.Warn("main", "No MAP server found. On the iPhone: Settings > Bluetooth > (i) next to this PC > Show Notifications.");
    return 1;
}

using var mas = new MasClient(capture);

if (!await mas.ConnectAsync(service))
{
    return 1;
}

using var mns = new MnsServer(capture, async mapEvent =>
{
    Log.Info("event", $"{mapEvent.Type} handle={mapEvent.Handle} folder={mapEvent.Folder} type={mapEvent.MessageType}");

    if (mapEvent.Type == "NewMessage")
    {
        var message = await mas.GetMessageAsync(mapEvent.Handle);
        var elapsed = (DateTimeOffset.Now - mapEvent.ReceivedAt).TotalMilliseconds;
        Log.Info("event", $"Fetched {elapsed:F0} ms after the event: {(message is null ? "(failed)" : BMessage.Summarize(message))}");
    }
});

await mns.StartAsync();
await mas.ListFoldersAsync();
await mas.ListMessagesAsync("inbox", 10);
await mas.ListMessagesAsync("sent", 5);
await mas.SetNotificationsAsync(true);

Log.Info("main", "Commands: list <folder> [count], get <handle>, read <handle>, unread <handle>, folders, notify on|off, send <number> <text>, q");

while (Console.ReadLine() is { } line)
{
    var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length == 0)
    {
        continue;
    }

    try
    {
        switch (parts[0])
        {
            case "q":
                await mas.SetNotificationsAsync(false);
                await mas.DisconnectAsync();
                return 0;
            case "list" when parts.Length > 1:
                await mas.ListMessagesAsync(parts[1], parts.Length > 2 && ushort.TryParse(parts[2], out var count) ? count : (ushort)10);
                break;
            case "get" when parts.Length > 1:
                var message = await mas.GetMessageAsync(parts[1]);
                Log.Info("mas", message is null ? "(failed)" : BMessage.Summarize(message));
                break;
            case "read" when parts.Length > 1:
                await mas.SetReadAsync(parts[1], true);
                break;
            case "unread" when parts.Length > 1:
                await mas.SetReadAsync(parts[1], false);
                break;
            case "folders":
                await mas.ListFoldersAsync();
                break;
            case "notify" when parts.Length > 1:
                await mas.SetNotificationsAsync(parts[1] == "on");
                break;
            case "send" when parts.Length > 2:
                await mas.PushMessageAsync(parts[1], parts[2]);
                break;
            default:
                Log.Warn("main", $"Unknown command: {line}");
                break;
        }
    }
    catch (Exception exception)
    {
        Log.Warn("main", $"{exception.GetType().Name}: {exception.Message}");
    }
}

return 0;

static string FindSpikeRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null && directory.Name != "S2-map")
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? Environment.CurrentDirectory;
}
