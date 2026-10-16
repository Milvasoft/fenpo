using Fenpo.Protocol.V1;

namespace Fenpo.Core.Channel;

/// <summary>
/// A phone paired with the Fenpo app (Wi‑Fi pairing, S5): its id, name, the BLE channel key the PC gave it and the
/// fingerprint of the certificate it used, pinned for every later TLS connection.
/// </summary>
/// <param name="DeviceId">Phone's PairingRequest.device_id.</param>
/// <param name="Name">Phone's name.</param>
/// <param name="BleKey">PairingResult.ble_channel_key (32 bytes).</param>
/// <param name="CertificateSha256">SHA-256 of the phone's TLS certificate (DER), lower-case hex.</param>
public sealed record PairedAppDevice(string DeviceId, string Name, byte[] BleKey, string CertificateSha256 = "");

/// <summary>
/// Phones paired with the Fenpo app.
/// </summary>
public interface IPairedAppDevices
{
    /// <summary>
    /// Gets the paired phones.
    /// </summary>
    IReadOnlyList<PairedAppDevice> All { get; }

    /// <summary>
    /// Adds or replaces a paired phone.
    /// </summary>
    /// <param name="device">Phone.</param>
    void Save(PairedAppDevice device);
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

    /// <summary>
    /// Starts both counters over for a phone that just got a new key (nonces only have to be unique per key).
    /// </summary>
    /// <param name="deviceId">Phone id.</param>
    void Reset(string deviceId);
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

/// <summary>
/// Builds the PC's Hello, the same on the BLE and the Wi‑Fi channel.
/// </summary>
public static class PcHello
{
    /// <summary>
    /// Creates the PC's Hello.
    /// </summary>
    /// <param name="identity">PC identity.</param>
    /// <returns>The Hello message.</returns>
    public static Hello Create(PcIdentity identity) => new()
    {
        DeviceId = identity.DeviceId,
        DeviceName = identity.DeviceName,
        Platform = DevicePlatform.Windows,
        AppVersion = identity.AppVersion,
        ProtocolVersions = { 1 },
        Capabilities =
        {
            Capability.DeviceState,
            Capability.Clipboard,
            Capability.OpenUrl,
            Capability.Ring,
            Capability.LockPc,
            Capability.FileTransfer,
            Capability.NotificationMirror,
            Capability.Media,
        },
        OsVersion = identity.OsVersion,
        Model = identity.Model,
    };
}
