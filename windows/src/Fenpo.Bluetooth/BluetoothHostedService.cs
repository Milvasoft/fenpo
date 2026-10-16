using Fenpo.Bluetooth.Ams;
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
    private readonly PhoneStatusTracker _status = status;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<BluetoothHostedService> _logger = logger;

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
        _status.Attach();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var phone = await _locator.FindAsync(_settings.Current.PhoneDeviceId, stoppingToken);

                if (phone is not null && await _link.StartAsync(phone.LeDeviceId, stoppingToken))
                {
                    Phone = phone;
                    _messages.UsePhone(phone.ClassicDeviceId);
                    _contacts.UsePhone(phone.ClassicDeviceId);
                    _settings.Update(s => s with { PhoneDeviceId = phone.LeDeviceId });
                    _status.Update(s => s with { Name = phone.Name, State = _link.IsConnected ? PhoneConnectionState.Connecting : PhoneConnectionState.Disconnected });
                    return;
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

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _messages.DisposeAsync();
        await _link.DisposeAsync();
    }
}
