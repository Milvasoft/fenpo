using System.Text;
using Fenpo.Bluetooth.Ancs;
using Fenpo.Bluetooth.Gatt;
using Fenpo.Bluetooth.Link;
using Fenpo.Core.Devices;
using Microsoft.Extensions.Logging;

namespace Fenpo.Bluetooth.Status;

/// <summary>
/// Combines the link state, Battery, Device Information, Current Time and ANCS availability into one
/// <see cref="PhoneStatus"/>.
/// </summary>
public sealed class PhoneStatusTracker(IphoneLink link, AncsClient ancs, ILogger<PhoneStatusTracker> logger) : IPhoneStatusSource
{
    private readonly IphoneLink _link = link;
    private readonly AncsClient _ancs = ancs;
    private readonly ILogger<PhoneStatusTracker> _logger = logger;
    private readonly Lock _lock = new();
    private PhoneStatus _current = PhoneStatus.Unknown;

    /// <inheritdoc/>
    public event EventHandler<PhoneStatus>? Changed;

    /// <inheritdoc/>
    public PhoneStatus Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Starts following the link and the clients.
    /// </summary>
    public void Attach()
    {
        _link.ConnectionChanged += (_, connected) => Update(s => s with
        {
            // Windows may report "connected" again after discovery already finished; that must not undo Connected.
            State = !connected ? PhoneConnectionState.Disconnected : s.State == PhoneConnectionState.Connected ? PhoneConnectionState.Connected : PhoneConnectionState.Connecting,
            BluetoothConnected = connected,
        });
        _link.ServicesReady += (sender, services) => _ = ReadServicesAsync(services);
        _link.BrokenBondDetected += (_, _) => Update(s => s with { State = PhoneConnectionState.BrokenBond });
        _ancs.AvailabilityChanged += (_, available) => Update(s => s with { NotificationsAvailable = available });
    }

    /// <summary>
    /// Updates the status, for example when no phone is paired or the Fenpo channel connects.
    /// </summary>
    /// <param name="change">Produces the new status.</param>
    public void Update(Func<PhoneStatus, PhoneStatus> change)
    {
        PhoneStatus updated;

        lock (_lock)
        {
            updated = change(_current);

            if (updated == _current)
            {
                return;
            }

            _current = updated;
        }

        Changed?.Invoke(this, updated);
    }

    private async Task ReadServicesAsync(LinkServices services)
    {
        Update(s => s with { State = PhoneConnectionState.Connected, BluetoothConnected = true, Name = _link.Name });

        try
        {
            if (services.Services.TryGetValue(GattUuids.DeviceInformation, out var dis) && await dis.GetCharacteristicAsync(GattUuids.ModelNumber) is { } model && await model.ReadAsync() is { } modelValue)
            {
                Update(s => s with { ModelIdentifier = Encoding.UTF8.GetString(modelValue).TrimEnd('\0') });
            }

            if (services.Services.TryGetValue(GattUuids.CurrentTime, out var cts) && await cts.GetCharacteristicAsync(GattUuids.CurrentTimeValue) is { } time && await time.ReadAsync() is { } timeValue && CurrentTimeParser.TryParse(timeValue, out var phoneTime))
            {
                Update(s => s with { ClockOffset = phoneTime - DateTime.Now });
            }

            if (services.Services.TryGetValue(GattUuids.Battery, out var battery) && await battery.GetCharacteristicAsync(GattUuids.BatteryLevel) is { } level)
            {
                if (await level.ReadAsync() is { Length: > 0 } levelValue)
                {
                    Update(s => s with { BatteryPercent = levelValue[0] });
                }

                await level.SubscribeAsync(data =>
                {
                    if (data.Length > 0)
                    {
                        Update(s => s with { BatteryPercent = data[0] });
                    }
                });
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogDebug(exception, "Status reads abandoned (generation {Generation})", services.Generation);
        }
    }
}
