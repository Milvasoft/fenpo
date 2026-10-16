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
/// seconds, so the pairing wizard sees the phone as soon as Windows has paired it.
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
        _status.ActiveChanged += (sender, kind) => _ = UseMessagesOfAsync(kind);
        _status.Attach();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var excluded = _settings.Current.NonApplePhoneIds;
                var preferred = excluded.Contains(_settings.Current.PhoneDeviceId ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? null : _settings.Current.PhoneDeviceId;
                var phone = await _locator.FindAsync(preferred, excluded, stoppingToken);

                if (phone is not null && await _link.StartAsync(phone.LeDeviceId, stoppingToken))
                {
                    Phone = phone;
                    await UseMessagesOfAsync(_status.Active);
                    _settings.Update(s => s with { PhoneDeviceId = phone.LeDeviceId });
                    _status.Update(s => s with { Name = phone.Name, State = _link.IsConnected ? PhoneConnectionState.Connecting : PhoneConnectionState.Disconnected });

                    // Stay with this phone until it turns out to be the wrong one.
                    await _relocate.WaitAsync(stoppingToken);
                    _logger.LogInformation("{Name} is not an iPhone; looking for the paired iPhone again", phone.Name);
                    Phone = null;
                    await _link.StopAsync();
                    await UseMessagesOfAsync(_status.Active);
                    continue;
                }

                _status.Update(s => s with { State = PhoneConnectionState.NotPaired });
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
