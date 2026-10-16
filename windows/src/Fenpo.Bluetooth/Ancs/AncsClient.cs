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

    // The iPhone refuses the subscription while "Share System Notifications" is off for this PC, and does not announce
    // when it is turned on: the subscription is tried again this often for as long as the link stays.
    private static readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(15);

    private readonly IphoneLink _link = link;
    private readonly ILogger<AncsClient> _logger = logger;
    private readonly SemaphoreSlim _controlPointGate = new(1, 1);
    private readonly AncsResponseReader _reader = new();
    private readonly Lock _readerLock = new();
    private readonly ConcurrentDictionary<string, string> _appNames = new();
    private Session? _session;
    private GattSubscription[] _subscriptions = [];
    private TaskCompletionSource<AncsResponse>? _pending;
    private LinkServices? _latest;
    private bool _refused;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Added;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Modified;

    /// <inheritdoc/>
    public event EventHandler<string>? Removed;

    /// <inheritdoc/>
    public event EventHandler<string>? Reset;

    /// <summary>
    /// Raised when notification access starts or stops working (ANCS visible and subscribed).
    /// </summary>
    public event EventHandler<bool>? AvailabilityChanged;

    /// <summary>
    /// Raised when the iPhone starts (true) or stops (false) refusing the ANCS subscription.
    /// </summary>
    public event EventHandler<bool>? RefusedChanged;

    /// <summary>
    /// Gets a value indicating whether notifications are flowing.
    /// </summary>
    public bool IsAvailable => _session is not null;

    /// <summary>
    /// Starts following the link: every service discovery rebuilds the ANCS session.
    /// </summary>
    public void Attach()
    {
        _link.ServicesReady += (sender, services) =>
        {
            _latest = services;
            _ = StartAsync(services);
        };
        _link.ServicesLost += (_, _) =>
        {
            _latest = null;
            SetRefused(false);
            Stop();
        };
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
            GattWriteResult? refusal = null;
            var dataSubscription = await dataSource.SubscribeAsync(OnDataSource, result => refusal = result);
            var sourceSubscription = dataSubscription is null ? null : await notificationSource.SubscribeAsync(data => OnNotificationSource(session, data), result => refusal = result);

            if (dataSubscription is null || sourceSubscription is null)
            {
                dataSubscription?.Dispose();

                if (!_refused)
                {
                    _logger.LogWarning("ANCS subscription failed (generation {Generation}): {Result}; notification sharing may be off for this PC on the iPhone, trying again every {Interval} s", services.Generation, refusal?.Describe() ?? "unknown", _retryInterval.TotalSeconds);
                }

                Stop();
                SetRefused(true);
                _ = RetryAsync(services);
                return;
            }

            // Held for the session: an unreferenced characteristic is collected and its notifications stop.
            ReplaceSubscriptions([dataSubscription, sourceSubscription]);

            _logger.LogInformation("ANCS started (generation {Generation})", services.Generation);
            SetRefused(false);
            AvailabilityChanged?.Invoke(this, true);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(exception, "ANCS start failed (generation {Generation}); waiting for the next discovery", services.Generation);
        }
    }

    private void SetRefused(bool refused)
    {
        if (_refused != refused)
        {
            _refused = refused;
            RefusedChanged?.Invoke(this, refused);
        }
    }

    private async Task RetryAsync(LinkServices services)
    {
        await Task.Delay(_retryInterval);

        // Only while the same link is up and nothing else started a session in between.
        if (ReferenceEquals(_latest, services) && _session is null)
        {
            await StartAsync(services);
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
        ReplaceSubscriptions([]);

        lock (_readerLock)
        {
            _reader.Reset();
            _pending?.TrySetCanceled();
        }

        Reset?.Invoke(this, string.Empty);
        AvailabilityChanged?.Invoke(this, false);
    }

    private void OnNotificationSource(Session session, byte[] data)
    {
        if (!AncsNotificationEvent.TryParse(data, out var notificationEvent))
        {
            _logger.LogWarning("Unreadable ANCS event ({Length} bytes)", data.Length);
            return;
        }

        _logger.LogDebug("ANCS {Event} uid {Uid} {Category}", notificationEvent.EventId, notificationEvent.Uid, notificationEvent.Category);

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
        _logger.LogDebug("ANCS processing started (generation {Generation})", session.Generation);

        await foreach (var notificationEvent in session.Events.Reader.ReadAllAsync())
        {
            _logger.LogDebug("ANCS requesting attributes for uid {Uid}", notificationEvent.Uid);

            try
            {
                var response = await RequestAsync(session, AncsCommands.GetNotificationAttributes(notificationEvent.Uid), AncsCommands.GetNotificationAttributesId, AncsCommands.NotificationAttributes.Count);

                if (response is null)
                {
                    _logger.LogWarning("No attributes for ANCS uid {Uid}", notificationEvent.Uid);
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
                _logger.LogWarning(exception, "ANCS request for uid {Uid} abandoned", notificationEvent.Uid);
            }
            catch (Exception exception)
            {
                // Runs fire-and-forget: anything that escapes here would silently stop every later notification.
                _logger.LogError(exception, "Handling ANCS uid {Uid} failed", notificationEvent.Uid);
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
        _logger.LogDebug("ANCS Control Point write 0x{Command:X2}", commandId);

        try
        {
            var pending = new TaskCompletionSource<AncsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_readerLock)
            {
                _reader.Expect(commandId, attributeCount);
                _pending = pending;
            }

            var result = await session.ControlPoint.WriteAsync(command);
            _logger.LogDebug("ANCS Control Point write done: {Result}", result.Describe());

            if (result.Status != GattCommunicationStatus.Success)
            {
                _logger.LogWarning("Control Point write failed: {Result}", result.Describe());
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

    private void ReplaceSubscriptions(GattSubscription[] subscriptions)
    {
        foreach (var old in Interlocked.Exchange(ref _subscriptions, subscriptions))
        {
            old.Dispose();
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
