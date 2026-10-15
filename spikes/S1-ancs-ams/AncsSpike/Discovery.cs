using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Spikes.S1;

/// <summary>
/// Service discovery and the one-shot reads (Device Information, Current Time, Battery).
/// </summary>
internal static class Discovery
{
    public static async Task<Dictionary<Guid, GattDeviceService>> DiscoverAsync(BluetoothLEDevice device, CaptureLog capture)
    {
        // ANCS and AMS appear only on an encrypted link. If they are missing, touch a characteristic to
        // make Windows encrypt with the stored bond, then discover again without the cache.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

            if (result.Status != GattCommunicationStatus.Success)
            {
                Log.Warn("gatt", $"Service discovery attempt {attempt}: {Gatt.Describe(result.Status, result.ProtocolError)}");
                await Task.Delay(2000);
                continue;
            }

            var services = result.Services.ToDictionary(s => s.Uuid);
            Log.Info("gatt", $"Discovery attempt {attempt}: {string.Join(", ", services.Keys.Select(Uuids.Name))}");

            if (services.ContainsKey(Uuids.Ancs) || attempt == 3)
            {
                return services;
            }

            if (services.TryGetValue(Uuids.Battery, out var battery) && await Gatt.GetCharacteristicAsync(battery, Uuids.BatteryLevel) is { } level)
            {
                await Gatt.ReadAsync(level, capture);
            }

            await Task.Delay(2000);
        }

        return [];
    }

    public static async Task PrintCharacteristicsAsync(IEnumerable<GattDeviceService> services)
    {
        foreach (var service in services)
        {
            var result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);

            if (result.Status != GattCommunicationStatus.Success)
            {
                Log.Warn("gatt", $"{Uuids.Name(service.Uuid)}: characteristics {Gatt.Describe(result.Status, result.ProtocolError)}");
                continue;
            }

            foreach (var characteristic in result.Characteristics)
            {
                Log.Info("gatt", $"{Uuids.Name(service.Uuid)} / {Uuids.Name(characteristic.Uuid)}: {characteristic.CharacteristicProperties}, protection={characteristic.ProtectionLevel}");
            }
        }
    }

    public static async Task TryServiceChangedAsync(Dictionary<Guid, GattDeviceService> services, CaptureLog capture)
    {
        if (!services.TryGetValue(Uuids.GenericAttribute, out var gattService))
        {
            Log.Info("gatt", "Generic Attribute service not exposed to apps; relying on GattServicesChanged");
            return;
        }

        if (await Gatt.GetCharacteristicAsync(gattService, Uuids.ServiceChanged) is { } serviceChanged)
        {
            await Gatt.SubscribeAsync(serviceChanged, capture, data => Log.Info("gatt", $"Service Changed: {Convert.ToHexString(data)}"));
        }
    }

    public static async Task ReadDeviceInformationAsync(Dictionary<Guid, GattDeviceService> services, CaptureLog capture)
    {
        if (!services.TryGetValue(Uuids.DeviceInformation, out var dis))
        {
            Log.Warn("dis", "Device Information service not visible");
            return;
        }

        var result = await dis.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);

        foreach (var characteristic in result.Characteristics.Where(c => c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read)))
        {
            if (await Gatt.ReadAsync(characteristic, capture) is { } value)
            {
                Log.Info("dis", $"{Uuids.Name(characteristic.Uuid)} = \"{Encoding.UTF8.GetString(value).TrimEnd('\0')}\" [{Convert.ToHexString(value)}]");
            }
        }
    }

    public static async Task ReadCurrentTimeAsync(Dictionary<Guid, GattDeviceService> services, CaptureLog capture)
    {
        if (!services.TryGetValue(Uuids.CurrentTime, out var cts) || await Gatt.GetCharacteristicAsync(cts, Uuids.CurrentTimeChar) is not { } characteristic)
        {
            Log.Warn("cts", "Current Time service not visible");
            return;
        }

        if (await Gatt.ReadAsync(characteristic, capture) is { Length: >= 7 } v)
        {
            var year = v[0] | (v[1] << 8);
            Log.Info("cts", $"iPhone time {year:D4}-{v[2]:D2}-{v[3]:D2} {v[4]:D2}:{v[5]:D2}:{v[6]:D2}, PC time {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Convert.ToHexString(v)}]");
        }
    }

    public static async Task ReadBatteryAsync(Dictionary<Guid, GattDeviceService> services, CaptureLog capture)
    {
        if (!services.TryGetValue(Uuids.Battery, out var battery) || await Gatt.GetCharacteristicAsync(battery, Uuids.BatteryLevel) is not { } level)
        {
            Log.Warn("bas", "Battery service not visible");
            return;
        }

        if (await Gatt.ReadAsync(level, capture) is { Length: > 0 } value)
        {
            Log.Info("bas", $"Battery {value[0]}%");
        }

        await Gatt.SubscribeAsync(level, capture, data => Log.Info("bas", $"Battery changed: {data[0]}%"));
    }
}
