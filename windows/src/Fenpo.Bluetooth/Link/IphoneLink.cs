using Fenpo.Bluetooth.Gatt;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Bluetooth.Link;

/// <summary>
/// A set of discovered GATT services, valid until <see cref="IphoneLink.ServicesLost"/>.
/// </summary>
/// <param name="Generation">Increases with every discovery; ANCS uids are only unique within one generation.</param>
/// <param name="Services">Services by UUID.</param>
public sealed record LinkServices(int Generation, IReadOnlyDictionary<Guid, GattDeviceService> Services);

/// <summary>
/// Keeps the Bluetooth LE link to the paired iPhone. The iPhone sends Service Changed after every reconnect, which
/// disposes all GATT objects and their subscriptions (S1), so after a reconnect or Service Changed the link waits for the
/// indications to settle, discovers again without the cache and announces fresh services; clients rebuild from there.
/// </summary>
public sealed class IphoneLink(ILogger<IphoneLink> logger) : IAsyncDisposable
{
    private static readonly TimeSpan _settleDelay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan _brokenBondWindow = TimeSpan.FromSeconds(15);
    private const int _brokenBondDiscoveries = 2;

    private readonly ILogger<IphoneLink> _logger = logger;
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly List<DateTimeOffset> _shortConnections = [];
    private BluetoothLEDevice? _device;
    private GattSession? _session;
    private CancellationTokenSource? _pendingDiscovery;
    private DateTimeOffset _connectedAt;
    private int _generation;
    private int _unencryptedDiscoveries;
    private bool _wasDisconnected = true;

    /// <summary>
    /// Raised after services were (re)discovered.
    /// </summary>
    public event EventHandler<LinkServices>? ServicesReady;

    /// <summary>
    /// Raised when the current services became invalid (disconnect or Service Changed).
    /// </summary>
    public event EventHandler? ServicesLost;

    /// <summary>
    /// Raised when the link connects or disconnects.
    /// </summary>
    public event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Raised when the phone refuses to encrypt the link with this PC's bond: it has no bond for this PC any more.
    /// Windows stops reconnecting by itself from then on.
    /// </summary>
    public event EventHandler? BrokenBondDetected;

    /// <summary>
    /// Raised when the link connects and drops again within seconds, repeatedly (S1). That is how a broken bond looks,
    /// but also how a phone at the edge of Bluetooth range looks (21 October), so the link keeps reconnecting.
    /// </summary>
    public event EventHandler? UnstableLinkDetected;

    /// <summary>
    /// Gets the phone's name.
    /// </summary>
    public string Name => _device?.Name ?? string.Empty;

    /// <summary>
    /// Gets the phone's LE address.
    /// </summary>
    public ulong Address => _device?.BluetoothAddress ?? 0;

    /// <summary>
    /// Gets a value indicating whether the link is up.
    /// </summary>
    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

    /// <summary>
    /// Gets the services of the current generation, or null.
    /// </summary>
    public LinkServices? Current { get; private set; }

    /// <summary>
    /// Opens the device and keeps the connection; discovery starts as soon as it is connected.
    /// </summary>
    /// <param name="deviceId">BluetoothLEDevice id of the paired phone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when the device cannot be opened.</returns>
    public async Task<bool> StartAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _device = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(cancellationToken);

        if (_device is null)
        {
            _logger.LogWarning("BluetoothLEDevice.FromIdAsync returned null for {DeviceId}", deviceId);
            return false;
        }

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
        _device.GattServicesChanged += (_, _) =>
        {
            _logger.LogInformation("Service Changed from {Name}", _device.Name);
            InvalidateAndRediscover("services changed");
        };

        // MaintainConnection makes Windows reconnect by itself when the phone comes back (S1: within a second).
        _session = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(cancellationToken);
        _session.MaintainConnection = true;

        _logger.LogInformation("Opened {Name} ({Address:X12}), status {Status}", _device.Name, _device.BluetoothAddress, _device.ConnectionStatus);
        ScheduleDiscovery("start", TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// Lets go of the device, for example when it turned out not to be an iPhone; listeners see the services and the
    /// connection go away.
    /// </summary>
    /// <returns>A task that completes when stopped.</returns>
    public async Task StopAsync()
    {
        await DisposeAsync();
        ServicesLost?.Invoke(this, EventArgs.Empty);
        ConnectionChanged?.Invoke(this, false);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _pendingDiscovery?.Cancel();
        Current = null;
        _session?.Dispose();
        _device?.Dispose();
        _session = null;
        _device = null;
        return ValueTask.CompletedTask;
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        var connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
        _logger.LogInformation("{Name}: {Status}", sender.Name, sender.ConnectionStatus);
        ConnectionChanged?.Invoke(this, connected);

        if (connected)
        {
            _connectedAt = DateTimeOffset.Now;

            if (_wasDisconnected)
            {
                _wasDisconnected = false;
                InvalidateAndRediscover("reconnected");
            }

            return;
        }

        _wasDisconnected = true;
        TrackShortConnection();
        Invalidate();
    }

    private void TrackShortConnection()
    {
        if (DateTimeOffset.Now - _connectedAt > _brokenBondWindow)
        {
            return;
        }

        lock (_lock)
        {
            _shortConnections.Add(DateTimeOffset.Now);
            _shortConnections.RemoveAll(t => DateTimeOffset.Now - t > TimeSpan.FromMinutes(1));

            if (_shortConnections.Count < 3)
            {
                return;
            }

            _shortConnections.Clear();
        }

        _logger.LogWarning("{Name} keeps dropping right after connecting: out of range, or a broken bond", Name);
        UnstableLinkDetected?.Invoke(this, EventArgs.Empty);
    }

    // A phone without this PC's bond asks its user to pair whenever the link tries to encrypt, for example a phone that
    // was paired with this PC once and forgot it, while it is used with another PC in the same house. Reconnecting would
    // ask again and again, so Windows stops keeping the connection.
    private void ReportBrokenBond(string why)
    {
        _logger.LogWarning("{Name}: {Why}; the bond looks broken, so the link stops reconnecting", Name, why);

        if (_session is { } session)
        {
            session.MaintainConnection = false;
        }

        BrokenBondDetected?.Invoke(this, EventArgs.Empty);
    }

    // Only the phone's own answer counts: an attribute error for missing authentication or encryption. A read that is
    // merely unreachable (the phone walked away mid-read) says nothing about the bond.
    private static bool RefusedEncryption(GattReadResult read)
    {
        return read.Status == GattCommunicationStatus.ProtocolError
            && read.ProtocolError is { } error
            && (error == GattProtocolError.InsufficientAuthentication
                || error == GattProtocolError.InsufficientEncryption
                || error == GattProtocolError.InsufficientEncryptionKeySize);
    }

    private void TrackEncryption(bool failed)
    {
        if (!failed)
        {
            _unencryptedDiscoveries = 0;
            return;
        }

        if (++_unencryptedDiscoveries >= _brokenBondDiscoveries)
        {
            _unencryptedDiscoveries = 0;
            ReportBrokenBond("the link cannot be encrypted");
        }
    }

    private void InvalidateAndRediscover(string reason)
    {
        Invalidate();
        ScheduleDiscovery(reason, _settleDelay);
    }

    private void Invalidate()
    {
        if (Current is null)
        {
            return;
        }

        Current = null;
        ServicesLost?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleDiscovery(string reason, TimeSpan delay)
    {
        CancellationTokenSource cts;

        lock (_lock)
        {
            _pendingDiscovery?.Cancel();
            _pendingDiscovery = cts = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                await DiscoverAsync(reason, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // A newer event scheduled another discovery.
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Discovery after {Reason} failed", reason);
            }
        });
    }

    private async Task DiscoverAsync(string reason, CancellationToken cancellationToken)
    {
        await _discoveryGate.WaitAsync(cancellationToken);

        try
        {
            if (_device is null)
            {
                return;
            }

            // ANCS shows up only on an encrypted link; touching the battery level makes Windows encrypt with the bond.
            // When that read fails the phone has no bond for this PC and is showing its user a pairing request; reading
            // again would show another one, so the discovery ends there.
            var unencrypted = false;

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);

                if (result.Status != GattCommunicationStatus.Success)
                {
                    _logger.LogInformation("Discovery ({Reason}) attempt {Attempt}: {Status}", reason, attempt, result.Status);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                var services = result.Services.GroupBy(s => s.Uuid).ToDictionary(g => g.Key, g => g.First());

                if (!services.ContainsKey(GattUuids.Ancs) && attempt < 3 && services.TryGetValue(GattUuids.Battery, out var battery) && await battery.GetCharacteristicAsync(GattUuids.BatteryLevel) is { } level)
                {
                    var read = await level.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);

                    if (!RefusedEncryption(read))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                        continue;
                    }

                    _logger.LogInformation("Discovery ({Reason}): the phone refused to encrypt ({Status} 0x{Error:X2})", reason, read.Status, read.ProtocolError);
                    unencrypted = true;
                }

                var generation = Interlocked.Increment(ref _generation);
                Current = new LinkServices(generation, services);
                _logger.LogInformation("Discovery ({Reason}) generation {Generation}: {Count} services, ANCS {Ancs}, AMS {Ams}", reason, generation, services.Count, services.ContainsKey(GattUuids.Ancs), services.ContainsKey(GattUuids.Ams));
                ServicesReady?.Invoke(this, Current);
                TrackEncryption(unencrypted);
                return;
            }
        }
        finally
        {
            _discoveryGate.Release();
        }
    }
}
