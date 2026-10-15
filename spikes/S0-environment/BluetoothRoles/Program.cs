using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

// Prints what the default Bluetooth adapter supports and which devices are already paired.
var adapter = await BluetoothAdapter.GetDefaultAsync();

if (adapter is null)
{
    Console.WriteLine("No Bluetooth adapter found.");
    return 1;
}

var adapterInfo = await DeviceInformation.CreateFromIdAsync(adapter.DeviceId);
var radio = await adapter.GetRadioAsync();

Console.WriteLine("Adapter");
Print("Name", adapterInfo.Name);
Print("Address", FormatAddress(adapter.BluetoothAddress));
Print("Radio state", radio?.State.ToString() ?? "unknown");
Print("IsClassicSupported", adapter.IsClassicSupported);
Print("IsLowEnergySupported", adapter.IsLowEnergySupported);
Print("IsCentralRoleSupported", adapter.IsCentralRoleSupported);
Print("IsPeripheralRoleSupported", adapter.IsPeripheralRoleSupported);
Print("AreLowEnergySecureConnectionsSupported", adapter.AreLowEnergySecureConnectionsSupported);
Print("AreClassicSecureConnectionsSupported", adapter.AreClassicSecureConnectionsSupported);
Print("IsAdvertisementOffloadSupported", adapter.IsAdvertisementOffloadSupported);
Print("IsExtendedAdvertisingSupported", adapter.IsExtendedAdvertisingSupported);
Print("MaxAdvertisementDataLength", adapter.MaxAdvertisementDataLength);

Console.WriteLine();
Console.WriteLine("Paired LE devices");
await PrintDevicesAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));

Console.WriteLine();
Console.WriteLine("Paired classic devices");
await PrintDevicesAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));

return radio?.State == RadioState.On ? 0 : 2;

static void Print(string name, object value) => Console.WriteLine($"  {name,-40} {value}");

static string FormatAddress(ulong address) => string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));

static async Task PrintDevicesAsync(string selector)
{
    var devices = await DeviceInformation.FindAllAsync(selector);

    if (devices.Count == 0)
    {
        Console.WriteLine("  (none)");
        return;
    }

    foreach (var device in devices)
    {
        Console.WriteLine($"  {device.Name}  [{device.Id}]");
    }
}
