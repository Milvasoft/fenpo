using System.Collections.Concurrent;
using System.Threading.Channels;
using Fenpo.Bluetooth.Gatt;
using Fenpo.Bluetooth.Link;
using Fenpo.Core.Notifications;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Bluetooth.Ancs;

/// <summary>
/// Reads iPhone notifications through the Apple Notification Center Service. Rebuilds its subscriptions every time the
/// link announces fresh services; keys are "generation:uid" because ANCS uids restart with every connection (S1).
/// </summary>
public sealed class AncsClient(IphoneLink link, ILogger<AncsClient> logger) : INotificationSource
{
    private static readonly TimeSpan _responseTimeout = TimeSpan.FromSeconds(5);

    private readonly IphoneLink _link = link;
    private readonly ILogger<AncsClient> _logger = logger;
    private readonly SemaphoreSlim _controlPointGate = new(1, 1);
    private readonly AncsResponseReader _reader = new();
    private readonly Lock _readerLock = new();
    private readonly ConcurrentDictionary<string, string> _appNames = new();
    private Session? _session;
    private TaskCompletionSource<AncsResponse>? _pending;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Added;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Modified;

    /// <inheritdoc/>
    public event EventHandler<string>? Removed;

    /// <inheritdoc/>
    public event EventHandler? Reset;

    /// <summary>
    /// Raised when notification access starts or stops working (ANCS visible and subscribed).
    /// </summary>
    public event EventHandler<bool>? AvailabilityChanged;

    /// <summary>
    /// Gets a value indicating whether notifications are flowing.
    /// </summary>
    public bool IsAvailable => _session is not null;

    /// <summary>
    /// Starts following the link: every service discovery rebuilds the ANCS session.
    /// </summary>
    public void Attach()
    {
        _link.ServicesReady += (sender, services) => _ = StartAsync(services);
        _link.ServicesLost += (_, _) => Stop();
    }

    /// <inheritdoc/>
    public async Task<bool> PerformActionAsync(string key, bool positive, CancellationToken cancellationToken = default)
    {
        var session = _session;

        if (session is null || !TryParseKey(key, out var generation, out var uid) || generation != session.Generation)
        {
            return false;
        }

        await _controlPointGate.WaitAsync(cancellationToken);

        try
        {
            var result = await session.ControlPoint.WriteAsync(AncsCommands.PerformNotificationAction(uid, positive));
            _logger.LogInformation("{Action} action on {Key}: {Result}", positive ? "Positive" : "Negative", key, result.Describe());
            return result.Status == GattCommunicationStatus.Success;
        }
        finally
        {
            _controlPointGate.Release();
        }
    }

    private async Task StartAsync(LinkServices services)
    {
        if (!services.Services.TryGetValue(GattUuids.Ancs, out var service))
        {
            _logger.LogWarning("ANCS not visible (generation {Generation}); notification sharing may be off on the iPhone", services.Generation);
            return;
        }

        try
        {
            var notificationSource = await service.GetCharacteristicAsync(GattUuids.AncsNotificationSource);
            var controlPoint = await service.GetCharacteristicAsync(GattUuids.AncsControlPoint);
            var dataSource = await service.GetCharacteristicAsync(GattUuids.AncsDataSource);

            if (notificationSource is null || controlPoint is null || dataSource is null)
            {
                _logger.LogWarning("ANCS characteristics missing (generation {Generation})", services.Generation);
                return;
            }

            var session = new Session(services.Generation, controlPoint, Channel.CreateUnbounded<AncsNotificationEvent>());
            _session = session;
            _ = Task.Run(() => ProcessAsync(session));

            // Data Source first so no response is lost; the Notification Source subscription starts the flow,
            // including every notification already on the phone (PreExisting).
            if (!await dataSource.SubscribeAsync(OnDataSource) || !await notificationSource.SubscribeAsync(data => OnNotificationSource(session, data)))
            {
                _logger.LogWarning("ANCS subscription failed (generation {Generation})", services.Generation);
                Stop();
                return;
            }

            _logger.LogInformation("ANCS started (generation {Generation})", services.Generation);
            AvailabilityChanged?.Invoke(this, true);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(exception, "ANCS start failed (generation {Generation}); waiting for the next discovery", services.Generation);
        }
    }

    private void Stop()
    {
        var session = Interlocked.Exchange(ref _session, null);

        if (session is null)
        {
            return;
        }

        session.Events.Writer.TryComplete();

        lock (_readerLock)
        {
            _reader.Reset();
            _pending?.TrySetCanceled();
        }

        Reset?.Invoke(this, EventArgs.Empty);
        AvailabilityChanged?.Invoke(this, false);
    }

    private void OnNotificationSource(Session session, byte[] data)
    {
        if (!AncsNotificationEvent.TryParse(data, out var notificationEvent))
        {
            return;
        }

        if (notificationEvent.EventId == AncsEventId.Removed)
        {
            Removed?.Invoke(this, Key(session.Generation, notificationEvent.Uid));
            return;
        }

        session.Events.Writer.TryWrite(notificationEvent);
    }

    private void OnDataSource(byte[] data)
    {
        lock (_readerLock)
        {
            if (_reader.Add(data, out var response))
            {
                _pending?.TrySetResult(response!);
            }
        }
    }

    private async Task ProcessAsync(Session session)
    {
        await foreach (var notificationEvent in session.Events.Reader.ReadAllAsync())
        {
            try
            {
                var response = await RequestAsync(session, AncsCommands.GetNotificationAttributes(notificationEvent.Uid), AncsCommands.GetNotificationAttributesId, AncsCommands.NotificationAttributes.Count);

                if (response is null)
                {
                    continue;
                }

                var appId = response[AncsAttribute.AppIdentifier];
                var notification = new PhoneNotification
                {
                    Key = Key(session.Generation, notificationEvent.Uid),
                    AppId = appId,
                    AppName = await GetAppNameAsync(session, appId),
                    Title = TextCleanup.Clean(response[AncsAttribute.Title], singleLine: true),
                    Subtitle = TextCleanup.Clean(response[AncsAttribute.Subtitle], singleLine: true),
                    Message = TextCleanup.Clean(response[AncsAttribute.Message]),
                    Date = AncsCommands.ParseDate(response[AncsAttribute.Date], DateTimeOffset.Now.Offset),
                    Category = notificationEvent.Category,
                    Flags = notificationEvent.Flags,
                    PositiveActionLabel = notificationEvent.HasPositiveAction ? NullIfEmpty(response[AncsAttribute.PositiveActionLabel]) : null,
                    NegativeActionLabel = notificationEvent.HasNegativeAction ? NullIfEmpty(response[AncsAttribute.NegativeActionLabel]) : null,
                };

                (notificationEvent.EventId == AncsEventId.Added ? Added : Modified)?.Invoke(this, notification);
            }
            catch (Exception exception) when (exception is ObjectDisposedException or System.Runtime.InteropServices.COMException or OperationCanceledException)
            {
                _logger.LogDebug(exception, "ANCS request for uid {Uid} abandoned", notificationEvent.Uid);
            }
        }
    }

    private async Task<string> GetAppNameAsync(Session session, string appId)
    {
        if (string.IsNullOrEmpty(appId))
        {
            return string.Empty;
        }

        if (_appNames.TryGetValue(appId, out var cached))
        {
            return cached;
        }

        var response = await RequestAsync(session, AncsCommands.GetAppDisplayName(appId), AncsCommands.GetAppAttributesId, 1);
        var name = response is null ? appId : TextCleanup.Clean(response.Values.GetValueOrDefault((byte)0), singleLine: true);
        _appNames[appId] = string.IsNullOrEmpty(name) ? appId : name;
        return _appNames[appId];
    }

    // Control Point writes are strictly sequential; each waits for its Data Source response.
    private async Task<AncsResponse?> RequestAsync(Session session, byte[] command, byte commandId, int attributeCount)
    {
        await _controlPointGate.WaitAsync();

        try
        {
            var pending = new TaskCompletionSource<AncsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_readerLock)
            {
                _reader.Expect(commandId, attributeCount);
                _pending = pending;
            }

            var result = await session.ControlPoint.WriteAsync(command);

            if (result.Status != GattCommunicationStatus.Success)
            {
                _logger.LogDebug("Control Point write failed: {Result}", result.Describe());
                return null;
            }

            var completed = await Task.WhenAny(pending.Task, Task.Delay(_responseTimeout));

            if (completed != pending.Task)
            {
                _logger.LogWarning("No Data Source response within {Timeout} s", _responseTimeout.TotalSeconds);
                return null;
            }

            return await pending.Task;
        }
        finally
        {
            lock (_readerLock)
            {
                _reader.Reset();
                _pending = null;
            }

            _controlPointGate.Release();
        }
    }

    private static string Key(int generation, uint uid) => $"{generation}:{uid}";

    private static bool TryParseKey(string key, out int generation, out uint uid)
    {
        var separator = key.IndexOf(':');
        generation = 0;
        uid = 0;
        return separator > 0 && int.TryParse(key[..separator], out generation) && uint.TryParse(key[(separator + 1)..], out uid);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record Session(int Generation, GattCharacteristic ControlPoint, Channel<AncsNotificationEvent> Events);
}
