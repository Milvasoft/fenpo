using Fenpo.Bluetooth.Gatt;
using Fenpo.Bluetooth.Link;
using Fenpo.Core.Media;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Bluetooth.Ams;

/// <summary>
/// Follows and controls the iPhone's media player through the Apple Media Service.
/// </summary>
public sealed class AmsClient(IphoneLink link, ILogger<AmsClient> logger) : IMediaSource
{
    private readonly IphoneLink _link = link;
    private readonly ILogger<AmsClient> _logger = logger;
    private readonly Lock _lock = new();
    private GattCharacteristic? _remoteCommand;
    private GattSubscription[] _subscriptions = [];
    private GattCharacteristic? _entityAttribute;
    private NowPlaying _current = NowPlaying.Empty;

    /// <inheritdoc/>
    public event EventHandler<NowPlaying>? Changed;

    /// <inheritdoc/>
    public NowPlaying Current
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
    /// Starts following the link: every service discovery rebuilds the AMS registrations.
    /// </summary>
    public void Attach()
    {
        _link.ServicesReady += (sender, services) => _ = StartAsync(services);
        _link.ServicesLost += (_, _) =>
        {
            _remoteCommand = null;
            _entityAttribute = null;
            Publish(NowPlaying.Empty);
        };
    }

    /// <inheritdoc/>
    public async Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        var remoteCommand = _remoteCommand;

        if (remoteCommand is null)
        {
            return false;
        }

        try
        {
            var result = await remoteCommand.WriteAsync([(byte)command]);
            _logger.LogDebug("{Command}: {Result}", command, result.Describe());
            return result.Status == GattCommunicationStatus.Success;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private async Task StartAsync(LinkServices services)
    {
        if (!services.Services.TryGetValue(GattUuids.Ams, out var service))
        {
            _logger.LogInformation("AMS not visible (generation {Generation})", services.Generation);
            return;
        }

        try
        {
            var remoteCommand = await service.GetCharacteristicAsync(GattUuids.AmsRemoteCommand);
            var entityUpdate = await service.GetCharacteristicAsync(GattUuids.AmsEntityUpdate);
            _entityAttribute = await service.GetCharacteristicAsync(GattUuids.AmsEntityAttribute);

            if (remoteCommand is null || entityUpdate is null)
            {
                _logger.LogWarning("AMS characteristics missing (generation {Generation})", services.Generation);
                return;
            }

            var commands = await remoteCommand.SubscribeAsync(data => Publish(Current with { SupportedCommands = AmsProtocol.ParseSupportedCommands(data) }));
            var updates = await entityUpdate.SubscribeAsync(data => _ = OnEntityUpdateAsync(data, DateTimeOffset.Now));
            _remoteCommand = remoteCommand;

            // Held for the session: an unreferenced characteristic is collected and its notifications stop.
            foreach (var old in Interlocked.Exchange(ref _subscriptions, [.. new[] { commands, updates }.OfType<GattSubscription>()]))
            {
                old.Dispose();
            }

            foreach (var registration in AmsProtocol.Registrations)
            {
                await entityUpdate.WriteAsync(registration);
            }

            _logger.LogInformation("AMS started (generation {Generation})", services.Generation);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(exception, "AMS start failed (generation {Generation})", services.Generation);
        }
    }

    private async Task OnEntityUpdateAsync(byte[] data, DateTimeOffset at)
    {
        if (!AmsEntityUpdate.TryParse(data, out var update))
        {
            return;
        }

        // A value cut at the MTU is read in full from Entity Attribute.
        if (update.Truncated && _entityAttribute is { } entityAttribute)
        {
            try
            {
                await entityAttribute.WriteAsync([(byte)update.Entity, update.Attribute]);

                if (await entityAttribute.ReadAsync() is { } full)
                {
                    update = update with { Value = System.Text.Encoding.UTF8.GetString(full), Truncated = false };
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }

        lock (_lock)
        {
            _current = AmsProtocol.Apply(_current, update, at);
        }

        Changed?.Invoke(this, Current);
    }

    private void Publish(NowPlaying value)
    {
        lock (_lock)
        {
            _current = value;
        }

        Changed?.Invoke(this, value);
    }
}
