using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Fenpo.Bluetooth.Status;
using Fenpo.Core.Channel;
using Fenpo.Core.Devices;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Bluetooth.AppChannel;

/// <summary>
/// The PC side of the Fenpo BLE channel (③): the fixed Fenpo GATT service with <c>rx</c> (phone writes) and <c>tx</c>
/// (PC notifies), advertised so the iPhone app finds the PC (S4). Each connected phone gets a
/// <see cref="BleChannelSession"/>.
/// </summary>
public sealed class FenpoGattServer(
    IPairedAppDevices devices,
    IBleCounters counters,
    PcIdentity identity,
    PhoneStatusTracker status,
    ILogger<FenpoGattServer> logger) : IAsyncDisposable
{
    /// <summary>Fenpo service (protocol/README.md).</summary>
    public static readonly Guid ServiceUuid = Guid.Parse("1888047E-C8C5-45CC-AAEA-333A8859E79F");

    /// <summary>Phone to PC.</summary>
    public static readonly Guid RxUuid = Guid.Parse("ECC8D0D1-990A-4B02-A4FC-49FA647E7254");

    /// <summary>PC to phone.</summary>
    public static readonly Guid TxUuid = Guid.Parse("E9EC01E0-8C02-4B8E-902C-4F113E25DFC8");

    private readonly IPairedAppDevices _devices = devices;
    private readonly IBleCounters _counters = counters;
    private readonly PcIdentity _identity = identity;
    private readonly PhoneStatusTracker _status = status;
    private readonly ILogger<FenpoGattServer> _logger = logger;
    private static readonly TimeSpan _advertisingCheck = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private readonly GattServiceProviderAdvertisingParameters _advertising = new() { IsConnectable = true, IsDiscoverable = true };
    private Timer? _advertisingWatch;
    private GattServiceProvider? _provider;
    private GattLocalCharacteristic? _tx;
    private PcStatus _lastPcStatus = new();

    /// <summary>
    /// Raised when the Fenpo app on a phone says Hello over Bluetooth.
    /// </summary>
    public event EventHandler<(PairedAppDevice Device, Hello Hello)>? HelloReceived;

    /// <summary>
    /// Raised when a phone that said Hello leaves the channel (unsubscribed or disconnected).
    /// </summary>
    public event EventHandler<PairedAppDevice>? SessionClosed;

    /// <summary>
    /// Raised with the Bluetooth LE id of a phone that said Hello as Android (it is not an iPhone for the ANCS link).
    /// </summary>
    public event EventHandler<string>? AndroidPhoneSeen;

    /// <summary>
    /// Raised for messages from the phone app that the channel does not answer itself.
    /// </summary>
    public event EventHandler<(PairedAppDevice Device, Envelope Envelope)>? MessageReceived;

    /// <summary>
    /// Gets a value indicating whether the service is up and advertised.
    /// </summary>
    public bool IsRunning => _provider is not null;

    /// <summary>
    /// Publishes the service and starts advertising. Does nothing when the adapter cannot be a peripheral (S0).
    /// </summary>
    /// <returns>True when running.</returns>
    public async Task<bool> StartAsync()
    {
        if (_provider is not null)
        {
            return true;
        }

        var adapter = await BluetoothAdapter.GetDefaultAsync();

        if (adapter is null || !adapter.IsPeripheralRoleSupported)
        {
            _logger.LogWarning("This Bluetooth adapter cannot be a peripheral; the Fenpo BLE channel is off");
            return false;
        }

        var created = await GattServiceProvider.CreateAsync(ServiceUuid);

        if (created.Error != BluetoothError.Success)
        {
            _logger.LogWarning("Fenpo GATT service could not be created: {Error}", created.Error);
            return false;
        }

        var provider = created.ServiceProvider;

        // Encryption required: the first access bonds the phone, which the PC then uses for ANCS (protocol/README.md).
        var rx = await provider.Service.CreateCharacteristicAsync(RxUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
        });
        var tx = await provider.Service.CreateCharacteristicAsync(TxUuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
        });

        if (rx.Error != BluetoothError.Success || tx.Error != BluetoothError.Success)
        {
            _logger.LogWarning("Fenpo characteristics could not be created: rx {Rx}, tx {Tx}", rx.Error, tx.Error);
            return false;
        }

        _tx = tx.Characteristic;
        rx.Characteristic.WriteRequested += OnWriteRequested;
        _tx.SubscribedClientsChanged += OnSubscribedClientsChanged;

        // The first status after StartAdvertising is a short Aborted, then the real one (S4). The advertisement can also
        // stay Aborted or stop later (seen from Android, A1), which hides the PC; a check restarts it.
        provider.AdvertisementStatusChanged += (sender, args) => _logger.LogDebug("Fenpo advertisement {Status} ({Error})", args.Status, args.Error);
        provider.StartAdvertising(_advertising);
        _provider = provider;
        _advertisingWatch = new Timer(_ => KeepAdvertising(), null, _advertisingCheck, _advertisingCheck);
        _status.Changed += OnPhoneStatusChanged;
        _logger.LogInformation("Fenpo BLE channel advertised");
        return true;
    }

    /// <summary>
    /// Sends a message to a phone connected over Bluetooth.
    /// </summary>
    /// <param name="deviceId">Phone id (the Fenpo app's, not the Bluetooth one).</param>
    /// <param name="envelope">Message; at most 4096 bytes once sealed.</param>
    /// <returns>False when the phone is not on the channel or the message is too large for it.</returns>
    public bool Send(string deviceId, Envelope envelope)
    {
        var connection = _connections.Values.FirstOrDefault(c => c.Session.Device?.DeviceId == deviceId);

        if (connection is null)
        {
            return false;
        }

        try
        {
            return connection.Session.Send(envelope);
        }
        catch (ArgumentException)
        {
            // Larger than a sealed BLE message: it has to wait for Wi-Fi.
            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a phone is on the Bluetooth channel.
    /// </summary>
    /// <param name="deviceId">Phone id.</param>
    /// <returns>True when it said Hello and is still subscribed.</returns>
    public bool IsConnected(string deviceId) => _connections.Values.Any(c => c.Session.Device?.DeviceId == deviceId);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _status.Changed -= OnPhoneStatusChanged;
        _advertisingWatch?.Dispose();
        _advertisingWatch = null;
        _provider?.StopAdvertising();
        _provider = null;

        foreach (var connection in _connections.Values)
        {
            connection.Outgoing.Writer.TryComplete();
        }

        _connections.Clear();
        return ValueTask.CompletedTask;
    }

    private void KeepAdvertising()
    {
        if (_provider is not { } provider || provider.AdvertisementStatus is GattServiceProviderAdvertisementStatus.Started or GattServiceProviderAdvertisementStatus.StartedWithoutAllAdvertisementData)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Fenpo advertisement was {Status}; starting it again", provider.AdvertisementStatus);

            if (provider.AdvertisementStatus != GattServiceProviderAdvertisementStatus.Stopped)
            {
                provider.StopAdvertising();
            }

            provider.StartAdvertising(_advertising);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // Bluetooth is off or the adapter is gone; the next check tries again.
            _logger.LogDebug(exception, "Restarting the Fenpo advertisement failed");
        }
    }

    private async void OnWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();

        try
        {
            var request = await args.GetRequestAsync();

            if (request is null)
            {
                return;
            }

            var connection = ConnectionFor(args.Session);

            if (connection.Session.OnChunk(request.Value.ToArray()) is { } dropped)
            {
                _logger.LogWarning("Fenpo BLE message from {Device} dropped: {Reason}", args.Session.DeviceId.Id, dropped);
            }

            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                request.Respond();
            }
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            _logger.LogDebug(exception, "Fenpo rx write abandoned");
        }
    }

    private void OnSubscribedClientsChanged(GattLocalCharacteristic sender, object args)
    {
        var subscribed = sender.SubscribedClients.Select(c => c.Session.DeviceId.Id).ToHashSet();

        foreach (var client in sender.SubscribedClients)
        {
            // The notification size is the true limit per phone (Android: MTU 517, 514 per notification).
            ConnectionFor(client.Session).Session.PayloadLength = Math.Max(1, client.MaxNotificationSize - BleChunks.HeaderLength);
        }

        foreach (var gone in _connections.Keys.Where(id => !subscribed.Contains(id)).ToList())
        {
            Close(gone);
        }
    }

    private void Close(string id)
    {
        if (!_connections.TryRemove(id, out var connection))
        {
            return;
        }

        connection.Outgoing.Writer.TryComplete();

        if (connection.Session.Device is { } device)
        {
            SessionClosed?.Invoke(this, device);
        }
    }

    private Connection ConnectionFor(GattSession gattSession) => _connections.GetOrAdd(gattSession.DeviceId.Id, id =>
    {
        // Chunks fit the largest notification: ATT MTU minus the ATT and chunk headers.
        var payload = BleChunks.PayloadLength(Math.Max(23, (int)gattSession.MaxPduSize));
        _logger.LogInformation("Fenpo BLE channel opened by {Device} (PDU {Pdu})", id, gattSession.MaxPduSize);
        var session = new BleChannelSession(_devices, _counters, _identity, CurrentPcStatus, payload);
        var connection = new Connection(id, session, Channel.CreateUnbounded<byte[]>());
        session.ChunkReady += (sender, chunk) => connection.Outgoing.Writer.TryWrite(chunk);
        session.HelloReceived += (sender, hello) =>
        {
            _logger.LogInformation("Fenpo app on {Phone} said hello ({Platform} {Os})", session.Device?.Name, hello.Platform, hello.OsVersion);

            if (hello.Platform == DevicePlatform.Android)
            {
                AndroidPhoneSeen?.Invoke(this, id);
            }

            if (session.Device is { } device)
            {
                HelloReceived?.Invoke(this, (device, hello));
            }
        };
        session.MessageReceived += (sender, envelope) =>
        {
            if (session.Device is { } device)
            {
                MessageReceived?.Invoke(this, (device, envelope));
            }
        };
        gattSession.SessionStatusChanged += (sender, e) =>
        {
            if (e.Status == GattSessionStatus.Closed)
            {
                Close(id);
            }
        };
        _ = Task.Run(() => SendLoopAsync(connection));
        return connection;
    });

    // Notifications go out one at a time and in order, to the phone they belong to.
    private async Task SendLoopAsync(Connection connection)
    {
        await foreach (var chunk in connection.Outgoing.Reader.ReadAllAsync())
        {
            var client = _tx?.SubscribedClients.FirstOrDefault(c => c.Session.DeviceId.Id == connection.Id);

            if (client is null || _tx is null)
            {
                continue;
            }

            try
            {
                var result = await _tx.NotifyValueAsync(chunk.AsBuffer(), client);

                if (result.Status != GattCommunicationStatus.Success)
                {
                    _logger.LogWarning("Fenpo tx notify failed: {Status}", result.Status);
                }
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ObjectDisposedException)
            {
                _logger.LogDebug(exception, "Fenpo tx notify abandoned");
            }
        }
    }

    private PcStatus CurrentPcStatus() => new()
    {
        NotificationsSubscribed = _status.Current.NotificationsAvailable,
        MessagesAvailable = _status.Current.MessagesAvailable,
    };

    private void OnPhoneStatusChanged(object? sender, PhoneStatus phoneStatus)
    {
        var current = CurrentPcStatus();

        if (current.Equals(_lastPcStatus))
        {
            return;
        }

        _lastPcStatus = current;

        foreach (var connection in _connections.Values)
        {
            connection.Session.SendPcStatus();
        }
    }

    private sealed record Connection(string Id, BleChannelSession Session, Channel<byte[]> Outgoing);
}
