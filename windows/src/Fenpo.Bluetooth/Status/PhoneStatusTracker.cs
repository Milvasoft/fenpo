using System.Text;
using Fenpo.Bluetooth.Ancs;
using Fenpo.Bluetooth.Gatt;
using Fenpo.Bluetooth.Link;
using Fenpo.Core.Devices;
using Fenpo.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Fenpo.Bluetooth.Status;

/// <summary>
/// The status of each paired phone and which one is in use. The iPhone's combines the link state, Battery, Device
/// Information, Current Time and ANCS availability; the Android phone's comes from the Fenpo channel. Everything that
/// shows "the phone" follows <see cref="Current"/>, the phone in use: the one chosen in the settings, or the other one
/// when only that one is paired, the iPhone first.
/// </summary>
public sealed class PhoneStatusTracker(IphoneLink link, AncsClient ancs, ISettingsStore settings, ILogger<PhoneStatusTracker> logger) : IPhoneStatusSource
{
    private readonly IphoneLink _link = link;
    private readonly AncsClient _ancs = ancs;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<PhoneStatusTracker> _logger = logger;
    private readonly Lock _lock = new();
    private PhoneStatus _iphone = PhoneStatus.Unknown;
    private PhoneStatus _android = PhoneStatus.Unknown;
    private PhoneKind _active = settings.Current.ActivePhone ?? PhoneKind.Iphone;
    private GattSubscription? _batterySubscription;

    /// <inheritdoc/>
    public event EventHandler<PhoneStatus>? Changed;

    /// <inheritdoc/>
    public event EventHandler<PhoneKind>? ActiveChanged;

    /// <inheritdoc/>
    public PhoneStatus Current
    {
        get
        {
            lock (_lock)
            {
                return _active == PhoneKind.Iphone ? _iphone : _android;
            }
        }
    }

    /// <inheritdoc/>
    public PhoneKind Active
    {
        get
        {
            lock (_lock)
            {
                return _active;
            }
        }
    }

    /// <inheritdoc/>
    public PhoneStatus StatusOf(PhoneKind kind)
    {
        lock (_lock)
        {
            return kind == PhoneKind.Iphone ? _iphone : _android;
        }
    }

    /// <inheritdoc/>
    public void Use(PhoneKind kind)
    {
        _settings.Update(s => s with { ActivePhone = kind });
        Choose();
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
        _link.UnstableLinkDetected += (_, _) => Update(s => s with { State = PhoneConnectionState.BrokenBond });
        _ancs.AvailabilityChanged += (_, available) => Update(s => s with { NotificationsAvailable = available, NotificationsRefused = !available && s.NotificationsRefused });
        _ancs.RefusedChanged += (_, refused) => Update(s => s with { NotificationsRefused = refused });

        // A forgotten iPhone or a new choice can change the phone in use.
        _settings.Changed += (_, _) => Choose();
    }

    /// <summary>
    /// Updates the iPhone's status, for example when no iPhone is paired or Fenpo on it connects.
    /// </summary>
    /// <param name="change">Produces the new status.</param>
    public void Update(Func<PhoneStatus, PhoneStatus> change) => Update(PhoneKind.Iphone, change);

    /// <summary>
    /// Updates one phone's status.
    /// </summary>
    /// <param name="kind">Phone.</param>
    /// <param name="change">Produces the new status.</param>
    public void Update(PhoneKind kind, Func<PhoneStatus, PhoneStatus> change)
    {
        PhoneStatus updated;
        bool shown;

        lock (_lock)
        {
            var before = kind == PhoneKind.Iphone ? _iphone : _android;
            updated = change(before);

            if (updated == before)
            {
                return;
            }

            if (kind == PhoneKind.Iphone)
            {
                _iphone = updated;
            }
            else
            {
                _android = updated;
            }

            shown = kind == _active;
        }

        // A phone that appears or goes away can change which one is in use.
        if (!Choose() && shown)
        {
            Changed?.Invoke(this, updated);
        }
    }

    // The iPhone found before counts as paired while its link is still being looked up after a start, so the phone in
    // use does not flip to the Android phone and back.
    private bool IsPaired(PhoneKind kind) => kind == PhoneKind.Iphone
        ? _iphone.State != PhoneConnectionState.NotPaired || _settings.Current.PhoneDeviceId is not null
        : _android.State != PhoneConnectionState.NotPaired;

    // The chosen phone while it is paired; otherwise whichever is.
    private bool Choose()
    {
        PhoneKind active;
        PhoneStatus current;

        lock (_lock)
        {
            var wanted = _settings.Current.ActivePhone ?? PhoneKind.Iphone;
            var other = wanted == PhoneKind.Iphone ? PhoneKind.Android : PhoneKind.Iphone;
            var wantedPaired = IsPaired(wanted);
            var otherPaired = IsPaired(other);
            active = !wantedPaired && otherPaired ? other : wanted;

            if (active == _active)
            {
                return false;
            }

            _active = active;
            current = active == PhoneKind.Iphone ? _iphone : _android;
        }

        _logger.LogInformation("Phone in use: {Phone}", active);
        ActiveChanged?.Invoke(this, active);
        Changed?.Invoke(this, current);
        return true;
    }

    private async Task ReadServicesAsync(LinkServices services)
    {
        // The name from the locator (the classic record's, as the user named the phone) wins over the LE record's "iPhone".
        Update(s => s with { State = PhoneConnectionState.Connected, BluetoothConnected = true, Name = string.IsNullOrEmpty(s.Name) ? _link.Name : s.Name });

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

                // Held until the next discovery: an unreferenced characteristic is collected and its notifications stop.
                var subscription = await level.SubscribeAsync(data =>
                {
                    if (data.Length > 0)
                    {
                        Update(s => s with { BatteryPercent = data[0] });
                    }
                });
                Interlocked.Exchange(ref _batterySubscription, subscription)?.Dispose();
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogDebug(exception, "Status reads abandoned (generation {Generation})", services.Generation);
        }
    }
}
