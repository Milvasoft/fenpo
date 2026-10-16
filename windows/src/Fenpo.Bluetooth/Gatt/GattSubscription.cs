using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;

namespace Fenpo.Bluetooth.Gatt;

/// <summary>
/// A notification subscription on a characteristic. Holding it keeps the characteristic alive: WinRT stops delivering
/// ValueChanged once the characteristic object is garbage collected, which ended live ANCS notifications after the
/// first burst. Dispose detaches the handler.
/// </summary>
internal sealed class GattSubscription : IDisposable
{
    private readonly GattCharacteristic _characteristic;
    private readonly TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GattSubscription"/> class and attaches the handler.
    /// </summary>
    /// <param name="characteristic">Characteristic.</param>
    /// <param name="onValue">Called with every notified value.</param>
    public GattSubscription(GattCharacteristic characteristic, Action<byte[]> onValue)
    {
        _characteristic = characteristic;
        _handler = (sender, args) => onValue(args.CharacteristicValue.ToArray());
        _characteristic.ValueChanged += _handler;
    }

    /// <inheritdoc/>
    public void Dispose() => _characteristic.ValueChanged -= _handler;
}
