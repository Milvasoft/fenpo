using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Spikes.S1;

/// <summary>
/// Owns the ANCS and AMS sessions for one iPhone. The iPhone sends Service Changed after every reconnect,
/// which disposes all service and characteristic objects and their subscriptions, so the link discovers
/// again and starts fresh sessions once the indications settle.
/// </summary>
internal sealed class IphoneLink(BluetoothLEDevice device, CaptureLog capture)
{
    private static readonly TimeSpan _settleDelay = TimeSpan.FromSeconds(4);

    private readonly BluetoothLEDevice _device = device;
    private readonly CaptureLog _capture = capture;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly Lock _lock = new();
    private CancellationTokenSource? _pendingRestart;
    private bool _started;
    private bool _wasDisconnected;

    public AncsSession? Ancs { get; private set; }

    public AmsSession? Ams { get; private set; }

    public int Restarts { get; private set; }

    public void Attach()
    {
        _device.GattServicesChanged += (_, _) =>
        {
            Log.Info("link", "GattServicesChanged");
            ScheduleRestart("services changed");
        };

        _device.ConnectionStatusChanged += (sender, _) =>
        {
            Log.Info("link", $"Connection status: {sender.ConnectionStatus}");

            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                _wasDisconnected = true;
            }
            else if (_wasDisconnected)
            {
                _wasDisconnected = false;
                ScheduleRestart("reconnected");
            }
        };
    }

    public async Task StartAsync(Func<Dictionary<Guid, GattDeviceService>, Task>? onServices = null)
    {
        await _startGate.WaitAsync();

        try
        {
            Ancs?.Stop();
            Ancs = null;
            Ams = null;

            var services = await Discovery.DiscoverAsync(_device, _capture);

            if (onServices is not null)
            {
                await onServices(services);
            }

            if (services.TryGetValue(Uuids.Ancs, out var ancsService))
            {
                var ancs = new AncsSession(ancsService, _capture);
                Log.Info("ancs", await ancs.StartAsync() ? "ANCS started" : "ANCS could not start");
                Ancs = ancs;
            }
            else
            {
                Log.Warn("ancs", "ANCS service not visible");
            }

            if (services.TryGetValue(Uuids.Ams, out var amsService))
            {
                var ams = new AmsSession(amsService, _capture);
                Log.Info("ams", await ams.StartAsync() ? "AMS started" : "AMS could not start");
                Ams = ams;
            }
            else
            {
                Log.Warn("ams", "AMS service not visible");
            }

            _started = true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private void ScheduleRestart(string reason)
    {
        if (!_started)
        {
            return;
        }

        CancellationTokenSource cts;

        lock (_lock)
        {
            _pendingRestart?.Cancel();
            _pendingRestart = cts = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_settleDelay, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            Restarts++;
            Log.Info("link", $"Restarting sessions ({reason}), restart #{Restarts}");
            await StartAsync(services => Discovery.ReadBatteryAsync(services, _capture));
        });
    }
}
