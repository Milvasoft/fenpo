namespace Fenpo.Core.Devices;

/// <summary>
/// How far the connection to the phone has come.
/// </summary>
public enum PhoneConnectionState
{
    /// <summary>No phone paired with this PC.</summary>
    NotPaired,

    /// <summary>Paired, phone not reachable right now.</summary>
    Disconnected,

    /// <summary>Connecting or rediscovering services.</summary>
    Connecting,

    /// <summary>Connected and services running.</summary>
    Connected,

    /// <summary>The link keeps dropping right after connecting: the bond is broken and needs re-pairing (S1).</summary>
    BrokenBond,
}

/// <summary>
/// Everything the home page, the tray and the local API show about the phone.
/// </summary>
public sealed record PhoneStatus
{
    /// <summary>
    /// Gets the status before anything is known.
    /// </summary>
    public static PhoneStatus Unknown { get; } = new();

    /// <summary>
    /// Gets the connection state.
    /// </summary>
    public PhoneConnectionState State { get; init; } = PhoneConnectionState.NotPaired;

    /// <summary>
    /// Gets the phone's Bluetooth name, e.g. "Buğra’nın iPhone’u".
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the model identifier from Device Information, e.g. "iPhone14,5".
    /// </summary>
    public string? ModelIdentifier { get; init; }

    /// <summary>
    /// Gets the marketing name for <see cref="ModelIdentifier"/>, e.g. "iPhone 13".
    /// </summary>
    public string? ModelName => ModelIdentifier is null ? null : ModelNames.Resolve(ModelIdentifier);

    /// <summary>
    /// Gets the OS version, only known through the Fenpo channel (Hello.os_version).
    /// </summary>
    public string? OsVersion { get; init; }

    /// <summary>
    /// Gets the battery level in percent.
    /// </summary>
    public int? BatteryPercent { get; init; }

    /// <summary>
    /// Gets a value indicating whether the phone is charging, when known.
    /// </summary>
    public bool? Charging { get; init; }

    /// <summary>
    /// Gets a value indicating whether the Bluetooth link is up.
    /// </summary>
    public bool BluetoothConnected { get; init; }

    /// <summary>
    /// Gets a value indicating whether the Fenpo data channel (Wi‑Fi) is up.
    /// </summary>
    public bool WifiConnected { get; init; }

    /// <summary>
    /// Gets a value indicating whether the phone is connected over USB.
    /// </summary>
    public bool UsbConnected { get; init; }

    /// <summary>
    /// Gets a value indicating whether notification access works (ANCS visible and subscribed).
    /// </summary>
    public bool NotificationsAvailable { get; init; }

    /// <summary>
    /// Gets a value indicating whether message access works (MAP).
    /// </summary>
    public bool MessagesAvailable { get; init; }

    /// <summary>
    /// Gets a value indicating whether the Fenpo iPhone app is connected (clipboard, files, camera need it).
    /// </summary>
    public bool AppConnected { get; init; }

    /// <summary>
    /// Gets a value indicating whether a phone is paired with Fenpo on it (an Android phone has no Bluetooth link to
    /// show otherwise).
    /// </summary>
    public bool HasAppPhone { get; init; }

    /// <summary>
    /// Gets the clock difference phone minus PC, from Current Time Service.
    /// </summary>
    public TimeSpan? ClockOffset { get; init; }
}
