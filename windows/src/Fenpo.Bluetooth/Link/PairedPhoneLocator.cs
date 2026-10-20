using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Fenpo.Bluetooth.Link;

/// <summary>
/// A paired phone as Windows knows it.
/// </summary>
/// <param name="LeDeviceId">BluetoothLEDevice id (ANCS, AMS, battery).</param>
/// <param name="ClassicDeviceId">BluetoothDevice id (MAP, PBAP), when the classic record exists too.</param>
/// <param name="Name">Device name.</param>
public sealed record PairedPhone(string LeDeviceId, string? ClassicDeviceId, string Name);

/// <summary>
/// Finds the paired iPhone. Windows Settings pairing creates both an LE and a classic record (S1); an LE record with the
/// Phone appearance is preferred, then one that has a classic record. Either way it has to be an iPhone by name: Windows
/// keeps the name the phone gave when pairing, which an iPhone reports as "iPhone" even after the user renames it, and an
/// Android phone paired in Windows Settings (or for the Fenpo app) is a paired LE phone too and must not become the
/// iPhone link (Fenpo 1.0.1 picked a Galaxy that way and never reached the user's iPhone). The two records of one phone have
/// different addresses but the same container id; names are not enough, since two iPhones in one house are often both
/// called "iPhone", and the wrong classic record makes the other iPhone ask its user to pair.
/// </summary>
public sealed class PairedPhoneLocator(ILogger<PairedPhoneLocator> logger)
{
    private const ushort _appearancePhone = 0x0040;
    private const string _containerIdProperty = "System.Devices.Aep.ContainerId";
    private static readonly string[] _properties = [_containerIdProperty];

    private readonly ILogger<PairedPhoneLocator> _logger = logger;

    /// <summary>
    /// Finds the phone to use.
    /// </summary>
    /// <param name="preferredLeDeviceId">Device id saved in the settings, tried first.</param>
    /// <param name="excludedLeDeviceIds">Devices that are not iPhones (Android phones seen on the Fenpo channel).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The phone, or null when none is paired.</returns>
    public async Task<PairedPhone?> FindAsync(string? preferredLeDeviceId, IReadOnlyCollection<string> excludedLeDeviceIds, CancellationToken cancellationToken = default)
    {
        var leDevices = (await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), _properties).AsTask(cancellationToken))
            .Where(d => !excludedLeDeviceIds.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var classicDevices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), _properties).AsTask(cancellationToken);

        DeviceInformation? chosen = leDevices.FirstOrDefault(d => d.Id == preferredLeDeviceId);

        if (chosen is null)
        {
            foreach (var candidate in leDevices)
            {
                using var device = await BluetoothLEDevice.FromIdAsync(candidate.Id).AsTask(cancellationToken);

                if (device?.Appearance?.RawValue is >= _appearancePhone and < _appearancePhone + 0x40 && IsIphone(candidate, classicDevices))
                {
                    chosen = candidate;
                    break;
                }
            }
        }

        chosen ??= leDevices.FirstOrDefault(le => ClassicOf(le, classicDevices) is not null && IsIphone(le, classicDevices));

        if (chosen is null)
        {
            _logger.LogInformation("No paired iPhone among {Count} LE device(s): {Names}", leDevices.Count, string.Join(", ", leDevices.Select(d => d.Name)));
            return null;
        }

        var classic = ClassicOf(chosen, classicDevices);
        _logger.LogInformation("Using {Name} (classic record {Classic})", chosen.Name, classic is null ? "missing" : "present");
        return new PairedPhone(chosen.Id, classic?.Id, chosen.Name);
    }

    private static bool IsIphone(DeviceInformation le, IReadOnlyList<DeviceInformation> classicDevices)
    {
        return le.Name.Contains("iphone", StringComparison.OrdinalIgnoreCase)
            || ClassicOf(le, classicDevices)?.Name.Contains("iphone", StringComparison.OrdinalIgnoreCase) == true;
    }

    // The classic record in the same container. Without container ids, a name match counts only when it is the only one.
    private static DeviceInformation? ClassicOf(DeviceInformation le, IReadOnlyList<DeviceInformation> classicDevices)
    {
        if (ContainerOf(le) is { } container)
        {
            return classicDevices.FirstOrDefault(c => ContainerOf(c) == container);
        }

        var named = classicDevices.Where(c => c.Name == le.Name).ToList();
        return named.Count == 1 ? named[0] : null;
    }

    private static Guid? ContainerOf(DeviceInformation device)
    {
        return device.Properties.TryGetValue(_containerIdProperty, out var value) && value is Guid container && container != Guid.Empty ? container : null;
    }

    /// <summary>
    /// Lists the Bluetooth LE devices Windows has paired.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Their BluetoothLEDevice ids.</returns>
    public async Task<IReadOnlyList<string>> PairedLeDeviceIdsAsync(CancellationToken cancellationToken = default)
    {
        var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(cancellationToken);
        return [.. devices.Select(d => d.Id)];
    }

    /// <summary>
    /// Finds the classic Bluetooth record that belongs to a paired LE record.
    /// </summary>
    /// <param name="leDeviceId">BluetoothLEDevice id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The BluetoothDevice id, or null.</returns>
    public async Task<string?> FindClassicOfAsync(string leDeviceId, CancellationToken cancellationToken = default)
    {
        var le = await DeviceInformation.CreateFromIdAsync(leDeviceId, _properties).AsTask(cancellationToken);
        var classicDevices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), _properties).AsTask(cancellationToken);
        return ClassicOf(le, classicDevices)?.Id;
    }

    /// <summary>
    /// Finds the classic Bluetooth record of a phone by name (an Android phone gets one with its LE bond, CTKD).
    /// </summary>
    /// <param name="name">Phone name as Fenpo on it reported it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The BluetoothDevice id, or null.</returns>
    public async Task<string?> FindClassicAsync(string name, CancellationToken cancellationToken = default)
    {
        var classicDevices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask(cancellationToken);
        return classicDevices.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
