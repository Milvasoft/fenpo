using Fenpo.Bluetooth.Link;
using Fenpo.Bluetooth.Proximity;
using Fenpo.Core.Devices;
using Fenpo.Core.Proximity;
using Fenpo.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// Locks Windows when the phone leaves (DECISIONS #29): advertisement RSSI and the link state feed the
/// <see cref="ProximityDetector"/>; one lock per away period, only while the setting is on.
/// </summary>
public sealed class ProximityLockService(
    AdvertisementRssiMonitor monitor,
    IphoneLink link,
    PhoneChannel channel,
    IPhoneStatusSource status,
    ISettingsStore settings,
    ILogger<ProximityLockService> logger) : BackgroundService
{
    private readonly AdvertisementRssiMonitor _monitor = monitor;
    private readonly IphoneLink _link = link;
    private readonly PhoneChannel _channel = channel;
    private readonly IPhoneStatusSource _status = status;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<ProximityLockService> _logger = logger;
    private readonly ProximityDetector _detector = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Raised when the phone's proximity state changes.
    /// </summary>
    public event EventHandler<ProximityState>? StateChanged;

    /// <summary>
    /// Gets the current state.
    /// </summary>
    public ProximityState State => _detector.State;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplySettings(_settings.Current);
        _settings.Changed += (_, current) => ApplySettings(current);
        // The iPhone's signal comes from its advertisements; an Android phone measures the PC on the BLE link instead.
        _monitor.Sample += (_, sample) => Locked(() =>
        {
            if (_status.Active == PhoneKind.Iphone)
            {
                _detector.AddSample(sample.At, sample.Rssi);
            }
        });
        _channel.AndroidRssi += (_, rssi) => Locked(() =>
        {
            if (_status.Active == PhoneKind.Android)
            {
                _detector.AddSample(DateTimeOffset.Now, rssi);
            }
        });
        _link.ConnectionChanged += (_, connected) => Locked(() =>
        {
            if (_status.Active == PhoneKind.Iphone)
            {
                _detector.SetConnected(connected, DateTimeOffset.Now);
            }
        });
        _status.Changed += (_, current) => Locked(() =>
        {
            if (_status.Active == PhoneKind.Android)
            {
                _detector.SetConnected(current.AppConnected, DateTimeOffset.Now);
            }
        });
        _status.ActiveChanged += (_, kind) => Locked(() => _detector.SetConnected(kind == PhoneKind.Iphone ? _link.IsConnected : _status.Current.AppConnected, DateTimeOffset.Now));
        _detector.StateChanged += (_, state) =>
        {
            _logger.LogInformation("Proximity {State} (average {Average:F0} dBm)", state, _detector.AverageDbm);
            StateChanged?.Invoke(this, state);
        };
        _detector.LockRequested += (_, _) => LockIfEnabled();
        _monitor.Start();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Locked(() => _detector.Tick(DateTimeOffset.Now));
        }
    }

    private void ApplySettings(AppSettings current) => Locked(() =>
    {
        _detector.ThresholdDbm = current.ProximityThresholdDbm;
        _detector.Delay = TimeSpan.FromSeconds(current.ProximityDelaySeconds);
    });

    private void LockIfEnabled()
    {
        if (!_settings.Current.ProximityLock)
        {
            return;
        }

        _logger.LogInformation("Phone stayed away for {Delay} s, locking", _settings.Current.ProximityDelaySeconds);

        if (!NativeMethods.LockWorkStation())
        {
            _logger.LogWarning("LockWorkStation failed");
        }
    }

    private void Locked(Action action)
    {
        lock (_lock)
        {
            action();
        }
    }
}
