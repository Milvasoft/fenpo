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
/// Finds the paired iPhone. Windows Settings pairing creates both an LE and a classic record (S1); the LE record with
/// the Phone appearance is preferred, then any paired LE device whose name matches a paired classic device.
/// </summary>
public sealed class PairedPhoneLocator(ILogger<PairedPhoneLocator> logger)
{
    private const ushort _appearancePhone = 0x0040;

    private readonly ILogger<PairedPhoneLocator> _logger = logger;

    /// <summary>
    /// Finds the phone to use.
    /// </summary>
    /// <param name="preferredLeDeviceId">Device id saved in the settings, tried first.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The phone, or null when none is paired.</returns>
    public async Task<PairedPhone?> FindAsync(string? preferredLeDeviceId, CancellationToken cancellationToken = default)
    {
        var leDevices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(cancellationToken);
        var classicDevices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask(cancellationToken);

        DeviceInformation? chosen = leDevices.FirstOrDefault(d => d.Id == preferredLeDeviceId);

        if (chosen is null)
        {
            foreach (var candidate in leDevices)
            {
                using var device = await BluetoothLEDevice.FromIdAsync(candidate.Id).AsTask(cancellationToken);

                if (device?.Appearance?.RawValue is >= _appearancePhone and < _appearancePhone + 0x40)
                {
                    chosen = candidate;
                    break;
                }
            }
        }

        chosen ??= leDevices.FirstOrDefault(le => classicDevices.Any(c => c.Name == le.Name) && le.Name.Contains("iphone", StringComparison.OrdinalIgnoreCase))
            ?? leDevices.FirstOrDefault(le => classicDevices.Any(c => c.Name == le.Name));

        if (chosen is null)
        {
            _logger.LogInformation("No paired phone among {Count} LE device(s)", leDevices.Count);
            return null;
        }

        var classic = classicDevices.FirstOrDefault(c => c.Name == chosen.Name);
        _logger.LogInformation("Using {Name} (classic record {Classic})", chosen.Name, classic is null ? "missing" : "present");
        return new PairedPhone(chosen.Id, classic?.Id, chosen.Name);
    }
}
