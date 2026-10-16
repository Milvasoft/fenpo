namespace Fenpo.Core.Channel;

/// <summary>
/// A phone paired with the Fenpo app (Wi‑Fi pairing, S5): its id, name and the BLE channel key the PC gave it.
/// </summary>
/// <param name="DeviceId">Phone's Hello.device_id.</param>
/// <param name="Name">Phone's name.</param>
/// <param name="BleKey">PairingResult.ble_channel_key (32 bytes).</param>
public sealed record PairedAppDevice(string DeviceId, string Name, byte[] BleKey);

/// <summary>
/// Phones paired with the Fenpo app.
/// </summary>
public interface IPairedAppDevices
{
    /// <summary>
    /// Gets the paired phones.
    /// </summary>
    IReadOnlyList<PairedAppDevice> All { get; }
}

/// <summary>
/// Counters for the BLE channel's nonces. The PC's send counter must never repeat for a key, across restarts too; the
/// last counter accepted from each phone guards against replays.
/// </summary>
public interface IBleCounters
{
    /// <summary>
    /// Gets the next send counter for a phone's key.
    /// </summary>
    /// <param name="deviceId">Phone id.</param>
    /// <returns>A counter never returned before for this phone.</returns>
    ulong NextSend(string deviceId);

    /// <summary>
    /// Accepts a received counter if it is newer than every one before.
    /// </summary>
    /// <param name="deviceId">Phone id.</param>
    /// <param name="counter">Counter from the nonce.</param>
    /// <returns>False for a replayed or old message.</returns>
    bool TryAcceptReceived(string deviceId, ulong counter);
}

/// <summary>
/// What the PC says about itself in Hello.
/// </summary>
/// <param name="DeviceId">Stable PC id.</param>
/// <param name="DeviceName">PC name.</param>
/// <param name="AppVersion">Fenpo version.</param>
/// <param name="OsVersion">Windows version.</param>
/// <param name="Model">Hardware model, if known.</param>
public sealed record PcIdentity(string DeviceId, string DeviceName, string AppVersion, string OsVersion, string Model);
