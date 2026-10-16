using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Bluetooth.Gatt;

/// <summary>
/// Thin helpers over WinRT GATT calls that report failures as null or false instead of status objects.
/// </summary>
internal static class GattExtensions
{
    /// <summary>
    /// Gets a characteristic from a service without the cache (S1: Windows caches empty lists from unencrypted links).
    /// </summary>
    public static async Task<GattCharacteristic?> GetCharacteristicAsync(this GattDeviceService service, Guid uuid)
    {
        var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached);
        return result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0 ? result.Characteristics[0] : null;
    }

    /// <summary>
    /// Attaches <paramref name="onValue"/> and enables notifications. The handler is attached first so nothing is missed.
    /// </summary>
    public static async Task<GattSubscription?> SubscribeAsync(this GattCharacteristic characteristic, Action<byte[]> onValue, Action<GattWriteResult>? failed = null)
    {
        var subscription = new GattSubscription(characteristic, onValue);
        var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

        if (result.Status != GattCommunicationStatus.Success)
        {
            subscription.Dispose();
            failed?.Invoke(result);
            return null;
        }

        return subscription;
    }

    /// <summary>
    /// Writes with response.
    /// </summary>
    public static async Task<GattWriteResult> WriteAsync(this GattCharacteristic characteristic, byte[] value) => await characteristic.WriteValueWithResultAsync(value.AsBuffer(), GattWriteOption.WriteWithResponse);

    /// <summary>
    /// Reads without the cache.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(this GattCharacteristic characteristic)
    {
        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
        return result.Status == GattCommunicationStatus.Success ? result.Value.ToArray() : null;
    }

    /// <summary>
    /// Describes a write result for logs.
    /// </summary>
    public static string Describe(this GattWriteResult result) => result.ProtocolError is { } error ? $"{result.Status} 0x{error:X2}" : result.Status.ToString();
}
