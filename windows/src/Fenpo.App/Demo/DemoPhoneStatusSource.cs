using Fenpo.Core.Devices;

namespace Fenpo.App.Demo;

/// <summary>
/// A made-up iPhone, connected over Bluetooth and Wi‑Fi with the Fenpo app open.
/// </summary>
public sealed class DemoPhoneStatusSource : IPhoneStatusSource
{
    private readonly PhoneStatus _iphone = new()
    {
        State = PhoneConnectionState.Connected,
        Name = DemoContent.Current.PhoneName,
        ModelIdentifier = "iPhone18,3",
        OsVersion = "26.0",
        BatteryPercent = 82,
        Charging = false,
        BluetoothConnected = true,
        WifiConnected = true,
        NotificationsAvailable = true,
        MessagesAvailable = true,
        AppConnected = true,
    };

    /// <inheritdoc/>
    public event EventHandler<PhoneStatus>? Changed;

    /// <inheritdoc/>
    public event EventHandler<PhoneKind>? ActiveChanged;

    /// <inheritdoc/>
    public PhoneStatus Current => _iphone;

    /// <inheritdoc/>
    public PhoneKind Active => PhoneKind.Iphone;

    /// <inheritdoc/>
    public PhoneStatus StatusOf(PhoneKind kind) => kind == PhoneKind.Iphone ? _iphone : PhoneStatus.Unknown;

    /// <inheritdoc/>
    public void Use(PhoneKind kind)
    {
        // There is one demo phone; switching keeps it.
        ActiveChanged?.Invoke(this, Active);
        Changed?.Invoke(this, _iphone);
    }
}
