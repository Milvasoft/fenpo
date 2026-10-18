using Fenpo.Bluetooth.Ams;
using Fenpo.Bluetooth.AppChannel;
using Fenpo.Bluetooth.Ancs;
using Fenpo.Bluetooth.Link;
using Fenpo.Bluetooth.Map;
using Fenpo.Bluetooth.Pbap;
using Fenpo.Bluetooth.Status;
using Fenpo.Core.Devices;
using Fenpo.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.Bluetooth;

/// <summary>
/// Finds the paired iPhone and keeps the link and its clients running. Until a phone is paired it looks again every few
/// seconds, so the pairing wizard sees the phone as soon as Windows has paired it. An iPhone whose bond broke is left
/// alone (<see cref="AppSettings.BrokenPhoneIds"/>) until it is paired again or forgotten.
/// </summary>
public sealed class BluetoothHostedService(
    PairedPhoneLocator locator,
    IphoneLink link,
    AncsClient ancs,
    AmsClient ams,
    MapMessageSource messages,
    PhonebookSource contacts,
    FenpoGattServer channel,
    PhoneStatusTracker status,
    ISettingsStore settings,
    ILogger<BluetoothHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan _searchInterval = TimeSpan.FromSeconds(5);

    private readonly PairedPhoneLocator _locator = locator;
    private readonly IphoneLink _link = link;
    private readonly AncsClient _ancs = ancs;
    private readonly AmsClient _ams = ams;
    private readonly MapMessageSource _messages = messages;
    private readonly PhonebookSource _contacts = contacts;
    private readonly FenpoGattServer _channel = channel;
    private readonly PhoneStatusTracker _status = status;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<BluetoothHostedService> _logger = logger;
    private readonly SemaphoreSlim _relocate = new(0, 1);

    /// <summary>
    /// Gets the phone in use, once found.
    /// </summary>
    public PairedPhone? Phone { get; private set; }

    /// <summary>
    /// Removes the iPhone's Bluetooth pairing from Windows (its LE and classic records), and those of iPhones whose bond
    /// broke, and stops using it; the search starts over, so a phone paired again afterwards is found.
    /// </summary>
    /// <returns>A task that completes when Windows removed the pairing.</returns>
    public async Task ForgetIphoneAsync()
    {
        var phone = Phone;
        var broken = _settings.Current.BrokenPhoneIds;
        _logger.LogInformation("Forgetting the iPhone {Name} and {Broken} with a broken bond", phone?.Name, broken.Count);
        await _link.StopAsync();
        Phone = null;
        _settings.Update(s => s with { PhoneDeviceId = null, BrokenPhoneIds = [] });

        var ids = new List<string>(new[] { phone?.LeDeviceId, phone?.ClassicDeviceId }.OfType<string>());

        foreach (var id in broken)
        {
            ids.Add(id);

            try
            {
                if (await _locator.FindClassicOfAsync(id) is { } classic)
                {
                    ids.Add(classic);
                }
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ArgumentException)
            {
                _logger.LogWarning(exception, "Looking up {Id} failed", id);
            }
        }

        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var device = await Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(id);
                var result = await device.Pairing.UnpairAsync();
                _logger.LogInformation("Unpairing {Id}: {Status}", id, result.Status);
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ArgumentException)
            {
                _logger.LogWarning(exception, "Unpairing {Id} failed", id);
            }
        }

        _status.Update(s => s with { State = PhoneConnectionState.NotPaired });

        if (_relocate.CurrentCount == 0)
        {
            _relocate.Release();
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ancs.Attach();
        _ams.Attach();
        _messages.Attach();
        _contacts.Attach();

        // The Fenpo service is advertised all the time so the iPhone app can find and wake for this PC (S4).
        await _channel.StartAsync();
        _channel.AndroidPhoneSeen += (sender, id) => OnAndroidPhoneSeen(id);
        _link.ServicesReady += (sender, services) => OnServicesReady(services);
        _link.BrokenBondDetected += (sender, args) => OnBrokenBond();
        _status.ActiveChanged += (sender, kind) => _ = UseMessagesOfAsync(kind);
        _status.Attach();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var broken = await ForgetUnpairedBrokenAsync(stoppingToken);
                var excluded = _settings.Current.NonApplePhoneIds.Concat(broken).ToList();
                var preferred = excluded.Contains(_settings.Current.PhoneDeviceId ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? null : _settings.Current.PhoneDeviceId;
                var phone = await _locator.FindAsync(preferred, excluded, stoppingToken);

                if (phone is not null && await _link.StartAsync(phone.LeDeviceId, stoppingToken))
                {
                    Phone = phone;
                    await UseMessagesOfAsync(_status.Active);
                    _settings.Update(s => s with { PhoneDeviceId = phone.LeDeviceId });
                    _status.Update(s => s with { Name = phone.Name, State = _link.IsConnected ? PhoneConnectionState.Connecting : PhoneConnectionState.Disconnected });

                    // Stay with this phone until it turns out to be the wrong one or its bond broke.
                    await _relocate.WaitAsync(stoppingToken);
                    _logger.LogInformation("Letting go of {Name}; looking for the paired iPhone again", phone.Name);
                    Phone = null;
                    await _link.StopAsync();
                    await UseMessagesOfAsync(_status.Active);
                    continue;
                }

                if (phone is null && _settings.Current.PhoneDeviceId is not null)
                {
                    _settings.Update(s => s with { PhoneDeviceId = null });
                }

                _status.Update(s => s with { State = broken.Count > 0 ? PhoneConnectionState.BrokenBond : PhoneConnectionState.NotPaired });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Looking for the paired phone failed");
            }

            await Task.Delay(_searchInterval, stoppingToken);
        }
    }

    // MAP and PBAP run against the phone in use: the iPhone's classic record, or the Android phone's, found by the name
    // Fenpo on it reported. The iPhone link keeps running either way (ANCS for later, proximity).
    private async Task UseMessagesOfAsync(PhoneKind kind)
    {
        try
        {
            if (kind == PhoneKind.Iphone)
            {
                _messages.UsePhone(Phone?.ClassicDeviceId);
                _contacts.UsePhone(Phone?.ClassicDeviceId);
                return;
            }

            var name = _status.StatusOf(PhoneKind.Android).Name;
            var classic = name.Length > 0 ? await _locator.FindClassicAsync(name) : null;
            _logger.LogInformation("Messages from the Android phone (classic record {Classic})", classic is null ? "missing" : "present");
            _messages.UsePhone(classic, PhoneKind.Android);
            _contacts.UsePhone(null);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or OperationCanceledException)
        {
            _logger.LogWarning(exception, "Switching messages to the {Phone} failed", kind);
        }
    }

    // An Android phone bonded for the Fenpo channel is a paired LE phone too, and it bonds again with a new address
    // after a lost bond. Whatever has none of the iPhone services is not the iPhone (S1 saw all three on the iPhone).
    private void OnServicesReady(LinkServices services)
    {
        var apple = services.Services.ContainsKey(Gatt.GattUuids.Ancs) || services.Services.ContainsKey(Gatt.GattUuids.Ams) || services.Services.ContainsKey(Gatt.GattUuids.AppleContinuity);

        if (!apple && Phone is { } phone)
        {
            _logger.LogInformation("{Name} has no iPhone services ({Count} services)", phone.Name, services.Services.Count);
            OnAndroidPhoneSeen(phone.LeDeviceId);
        }
    }

    // The phone keeps asking its user to pair while Fenpo connects to it; Fenpo lets go of it until it is paired again.
    private void OnBrokenBond()
    {
        if (Phone is not { } phone)
        {
            return;
        }

        if (!_settings.Current.BrokenPhoneIds.Contains(phone.LeDeviceId, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Update(s => s with { BrokenPhoneIds = [.. s.BrokenPhoneIds, phone.LeDeviceId] });
        }

        if (_relocate.CurrentCount == 0)
        {
            _relocate.Release();
        }
    }

    // Once Windows no longer has the pairing (removed in Settings, or paired again from scratch), the phone is no
    // longer left out.
    private async Task<IReadOnlyList<string>> ForgetUnpairedBrokenAsync(CancellationToken cancellationToken)
    {
        var broken = _settings.Current.BrokenPhoneIds;

        if (broken.Count == 0)
        {
            return broken;
        }

        var paired = await _locator.PairedLeDeviceIdsAsync(cancellationToken);
        var stillPaired = broken.Where(id => paired.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();

        if (stillPaired.Count != broken.Count)
        {
            _logger.LogInformation("{Count} iPhone(s) with a broken bond are no longer paired", broken.Count - stillPaired.Count);
            _settings.Update(s => s with { BrokenPhoneIds = stillPaired });
        }

        return stillPaired;
    }

    private void OnAndroidPhoneSeen(string leDeviceId)
    {
        if (!_settings.Current.NonApplePhoneIds.Contains(leDeviceId, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Update(s => s with { NonApplePhoneIds = [.. s.NonApplePhoneIds, leDeviceId] });
        }

        if (string.Equals(Phone?.LeDeviceId, leDeviceId, StringComparison.OrdinalIgnoreCase) && _relocate.CurrentCount == 0)
        {
            _relocate.Release();
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _channel.DisposeAsync();
        await _messages.DisposeAsync();
        await _link.DisposeAsync();
    }
}
