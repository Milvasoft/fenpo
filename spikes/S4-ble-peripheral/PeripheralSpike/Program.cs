using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

// Windows side of S4: the PC as GATT server with the fixed Fenpo service (protocol/README.md on the ios
// branch, commit beede75). Measures what Windows lets us put into the advertisement and logs every
// subscription and write, so the iPhone side (nRF Connect now, the Fenpo app later) has a counterpart.
Console.OutputEncoding = Encoding.UTF8;

var serviceUuid = new Guid("1888047E-C8C5-45CC-AAEA-333A8859E79F");
var rxUuid = new Guid("ECC8D0D1-990A-4B02-A4FC-49FA647E7254");
var txUuid = new Guid("E9EC01E0-8C02-4B8E-902C-4F113E25DFC8");

var adapter = await BluetoothAdapter.GetDefaultAsync();
Log("main", $"Adapter peripheral={adapter.IsPeripheralRoleSupported} extended={adapter.IsExtendedAdvertisingSupported} maxAdvData={adapter.MaxAdvertisementDataLength} offload={adapter.IsAdvertisementOffloadSupported}");

var created = await GattServiceProvider.CreateAsync(serviceUuid);

if (created.Error != BluetoothError.Success)
{
    Log("main", $"GattServiceProvider.CreateAsync failed: {created.Error}");
    return 1;
}

var provider = created.ServiceProvider;

var rxResult = await provider.Service.CreateCharacteristicAsync(rxUuid, new GattLocalCharacteristicParameters
{
    CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
    WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
    UserDescription = "rx",
});

var txResult = await provider.Service.CreateCharacteristicAsync(txUuid, new GattLocalCharacteristicParameters
{
    CharacteristicProperties = GattCharacteristicProperties.Notify,
    ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
    WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
    UserDescription = "tx",
});

if (rxResult.Error != BluetoothError.Success || txResult.Error != BluetoothError.Success)
{
    Log("main", $"Characteristic creation failed: rx={rxResult.Error} tx={txResult.Error}");
    return 1;
}

var rx = rxResult.Characteristic;
var tx = txResult.Characteristic;

rx.WriteRequested += async (_, args) =>
{
    using var deferral = args.GetDeferral();
    var request = await args.GetRequestAsync();

    if (request is null)
    {
        Log("rx", "GetRequestAsync returned null (access denied?)");
        return;
    }

    var data = request.Value.ToArray();
    Log("rx", $"{args.Session.DeviceId.Id} wrote {data.Length} bytes ({request.Option}) offset={request.Offset}: {Convert.ToHexString(data)} \"{Printable(data)}\"");

    if (request.Option == GattWriteOption.WriteWithResponse)
    {
        request.Respond();
    }
};

tx.SubscribedClientsChanged += (sender, _) =>
{
    Log("tx", $"{sender.SubscribedClients.Count} subscribed client(s)");

    foreach (var client in sender.SubscribedClients)
    {
        Log("tx", $"  {client.Session.DeviceId.Id} maxNotification={client.MaxNotificationSize} pdu={client.Session.MaxPduSize}");
        client.Session.SessionStatusChanged += (session, e) => Log("tx", $"  session {session.DeviceId.Id}: {e.Status} {e.Error}");
    }
};

provider.AdvertisementStatusChanged += (sender, args) => Log("adv", $"Status {args.Status}, error {args.Error}");

// "--probe": try each service data size for a few seconds and exit, to see what Windows accepts.
if (args.Contains("--probe"))
{
    foreach (var size in new[] { 0, 4, 8, 13, 20, 40 })
    {
        StartAdvertising(size);
        await Task.Delay(3000);
        Log("adv", $"After 3 s with {size} bytes: {provider.AdvertisementStatus}");
        provider.StopAdvertising();
        await Task.Delay(1000);
    }

    return 0;
}

StartAdvertising(0);

Log("main", "Commands: adv <service data bytes: 0|4|8|20>, send <text>, status, q");

while (Console.ReadLine() is { } line)
{
    var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length == 0)
    {
        continue;
    }

    switch (parts[0])
    {
        case "q":
            provider.StopAdvertising();
            return 0;
        case "adv" when parts.Length > 1 && int.TryParse(parts[1], out var size):
            provider.StopAdvertising();
            await Task.Delay(500);
            StartAdvertising(size);
            break;
        case "send" when parts.Length > 1:
            var payload = Encoding.UTF8.GetBytes(parts[1]);
            var results = await tx.NotifyValueAsync(payload.AsBuffer());
            Log("tx", $"Notified {payload.Length} bytes to {results.Count} client(s): {string.Join(", ", results.Select(r => $"{r.Status}{(r.ProtocolError is { } e ? $" 0x{e:X2}" : string.Empty)}"))}");
            break;
        case "status":
            Log("main", $"Advertisement {provider.AdvertisementStatus}, {tx.SubscribedClients.Count} subscribed client(s)");
            break;
        default:
            Log("main", $"Unknown command: {line}");
            break;
    }
}

return 0;

void StartAdvertising(int serviceDataLength)
{
    var parameters = new GattServiceProviderAdvertisingParameters
    {
        IsConnectable = true,
        IsDiscoverable = true,
    };

    if (serviceDataLength > 0)
    {
        // A stand-in for a short PC id, to see what fits next to the 128-bit service UUID.
        parameters.ServiceData = Enumerable.Range(1, serviceDataLength).Select(i => (byte)i).ToArray().AsBuffer();
    }

    try
    {
        provider.StartAdvertising(parameters);
        Log("adv", $"StartAdvertising with {serviceDataLength} bytes of service data: {provider.AdvertisementStatus}");
    }
    catch (Exception exception)
    {
        Log("adv", $"StartAdvertising with {serviceDataLength} bytes failed: {exception.GetType().Name} 0x{exception.HResult:X8} {exception.Message}");
    }
}

static string Printable(byte[] data) => new(Encoding.UTF8.GetString(data).Select(c => char.IsControl(c) ? '.' : c).ToArray());

static void Log(string area, string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{area}] {message}");
