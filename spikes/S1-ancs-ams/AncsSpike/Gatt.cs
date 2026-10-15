using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Spikes.S1;

/// <summary>
/// UUIDs of the iPhone services this spike looks at.
/// </summary>
internal static class Uuids
{
    public static readonly Guid Ancs = new("7905F431-B5CE-4E99-A40F-4B1E122D00D0");
    public static readonly Guid AncsNotificationSource = new("9FBF120D-6301-42D9-8C58-25E699A21DBD");
    public static readonly Guid AncsControlPoint = new("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");
    public static readonly Guid AncsDataSource = new("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");

    public static readonly Guid Ams = new("89D3502B-0F36-433A-8EF4-C502AD55F8DC");
    public static readonly Guid AmsRemoteCommand = new("9B3C81D8-57B1-4A8A-B8DF-0E56F7CA51C2");
    public static readonly Guid AmsEntityUpdate = new("2F7CABCE-808D-411F-9A0C-BB92BA96C102");
    public static readonly Guid AmsEntityAttribute = new("C6B2F38C-23AB-46D8-A6AB-A3A870BBD5D7");

    public static readonly Guid GenericAttribute = BluetoothUuidHelper.FromShortId(0x1801);
    public static readonly Guid ServiceChanged = BluetoothUuidHelper.FromShortId(0x2A05);
    public static readonly Guid DeviceInformation = BluetoothUuidHelper.FromShortId(0x180A);
    public static readonly Guid CurrentTime = BluetoothUuidHelper.FromShortId(0x1805);
    public static readonly Guid CurrentTimeChar = BluetoothUuidHelper.FromShortId(0x2A2B);
    public static readonly Guid Battery = BluetoothUuidHelper.FromShortId(0x180F);
    public static readonly Guid BatteryLevel = BluetoothUuidHelper.FromShortId(0x2A19);

    private static readonly Dictionary<Guid, string> _names = new()
    {
        [Ancs] = "ANCS",
        [AncsNotificationSource] = "ANCS Notification Source",
        [AncsControlPoint] = "ANCS Control Point",
        [AncsDataSource] = "ANCS Data Source",
        [Ams] = "AMS",
        [AmsRemoteCommand] = "AMS Remote Command",
        [AmsEntityUpdate] = "AMS Entity Update",
        [AmsEntityAttribute] = "AMS Entity Attribute",
        [new Guid("D0611E78-BBB4-4591-A5F8-487910AE4366")] = "Apple Continuity",
        [new Guid("9FA480E0-4967-4542-9390-D343DC5D04AE")] = "Apple Nearby",
        [BluetoothUuidHelper.FromShortId(0x1800)] = "Generic Access",
        [GenericAttribute] = "Generic Attribute",
        [ServiceChanged] = "Service Changed",
        [DeviceInformation] = "Device Information",
        [CurrentTime] = "Current Time",
        [CurrentTimeChar] = "Current Time (char)",
        [BluetoothUuidHelper.FromShortId(0x2A0F)] = "Local Time Information",
        [Battery] = "Battery",
        [BatteryLevel] = "Battery Level",
        [BluetoothUuidHelper.FromShortId(0x2A29)] = "Manufacturer Name",
        [BluetoothUuidHelper.FromShortId(0x2A24)] = "Model Number",
        [BluetoothUuidHelper.FromShortId(0x2A25)] = "Serial Number",
        [BluetoothUuidHelper.FromShortId(0x2A26)] = "Firmware Revision",
        [BluetoothUuidHelper.FromShortId(0x2A27)] = "Hardware Revision",
        [BluetoothUuidHelper.FromShortId(0x2A28)] = "Software Revision",
        [BluetoothUuidHelper.FromShortId(0x2A23)] = "System ID",
        [BluetoothUuidHelper.FromShortId(0x2A50)] = "PnP ID",
    };

    public static string Name(Guid uuid) => _names.TryGetValue(uuid, out var name) ? name : uuid.ToString();
}

/// <summary>
/// Small helpers around WinRT GATT calls.
/// </summary>
internal static class Gatt
{
    public static async Task<GattCharacteristic?> GetCharacteristicAsync(GattDeviceService service, Guid uuid)
    {
        var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached);

        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
        {
            Log.Warn("gatt", $"{Uuids.Name(uuid)} not found: {Describe(result.Status, result.ProtocolError)}");
            return null;
        }

        return result.Characteristics[0];
    }

    public static async Task<bool> SubscribeAsync(GattCharacteristic characteristic, CaptureLog capture, Action<byte[]> onValue)
    {
        var name = Uuids.Name(characteristic.Uuid);

        characteristic.ValueChanged += (_, args) =>
        {
            var data = args.CharacteristicValue.ToArray();
            capture.Write("notify", name, data);
            onValue(data);
        };

        var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

        if (result.Status != GattCommunicationStatus.Success)
        {
            Log.Warn("gatt", $"Subscribe {name} failed: {Describe(result.Status, result.ProtocolError)}");
            return false;
        }

        Log.Info("gatt", $"Subscribed to {name}");
        return true;
    }

    public static async Task<byte[]?> ReadAsync(GattCharacteristic characteristic, CaptureLog capture)
    {
        var name = Uuids.Name(characteristic.Uuid);
        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);

        if (result.Status != GattCommunicationStatus.Success)
        {
            Log.Warn("gatt", $"Read {name} failed: {Describe(result.Status, result.ProtocolError)}");
            return null;
        }

        var data = result.Value.ToArray();
        capture.Write("read", name, data);
        return data;
    }

    public static async Task<bool> WriteAsync(GattCharacteristic characteristic, byte[] data, CaptureLog capture)
    {
        var name = Uuids.Name(characteristic.Uuid);
        capture.Write("write", name, data);

        var result = await characteristic.WriteValueWithResultAsync(data.AsBuffer(), GattWriteOption.WriteWithResponse);

        if (result.Status != GattCommunicationStatus.Success)
        {
            Log.Warn("gatt", $"Write {name} [{Convert.ToHexString(data)}] failed: {Describe(result.Status, result.ProtocolError)}");
            return false;
        }

        return true;
    }

    public static string Describe(GattCommunicationStatus status, byte? protocolError) => protocolError is { } error
        ? $"{status}, protocol error 0x{error:X2} {ProtocolErrorName(error)}"
        : status.ToString();

    private static string ProtocolErrorName(byte error) => error switch
    {
        0x02 => "(read not permitted)",
        0x03 => "(write not permitted)",
        0x05 => "(insufficient authentication)",
        0x0F => "(insufficient encryption)",
        0xA0 => "(ANCS unknown command)",
        0xA1 => "(ANCS invalid command)",
        0xA2 => "(ANCS invalid parameter)",
        0xA3 => "(ANCS action failed)",
        _ => string.Empty,
    };
}
