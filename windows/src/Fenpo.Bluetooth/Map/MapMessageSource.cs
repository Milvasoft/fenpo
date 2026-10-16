using Fenpo.Bluetooth.Link;
using Fenpo.Bluetooth.Obex;
using Fenpo.Bluetooth.Status;
using Fenpo.Core.Devices;
using Fenpo.Core.Messages;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;

namespace Fenpo.Bluetooth.Map;

/// <summary>
/// The iPhone's messages over MAP: connects to the Message Access Server while the phone is in reach, keeps the
/// notification server registered and reconnects after a failure. New messages are first shown from ANCS; this source
/// is for the conversation history, read status and replies (S2). An Android phone in use has its own message access
/// server (Samsung: S10+); it is reached over its classic record whenever Fenpo on it is connected, not by the
/// iPhone's link.
/// </summary>
public sealed class MapMessageSource(
    IphoneLink link,
    PhoneStatusTracker status,
    MnsServer notifications,
    ILogger<MasClient> masLogger,
    ILogger<MapMessageSource> logger) : IMessageSource, IAsyncDisposable
{
    private static readonly TimeSpan _operationTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan _permissionRetry = TimeSpan.FromSeconds(20);

    private readonly IphoneLink _link = link;
    private readonly PhoneStatusTracker _status = status;
    private readonly MnsServer _notifications = notifications;
    private readonly ILogger<MasClient> _masLogger = masLogger;
    private readonly ILogger<MapMessageSource> _logger = logger;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private string? _classicDeviceId;
    private PhoneKind _kind = PhoneKind.Iphone;
    private MasClient? _session;
    private CancellationTokenSource? _retry;

    /// <inheritdoc/>
    public event EventHandler<MessagesAvailability>? AvailabilityChanged;

    /// <inheritdoc/>
    public event EventHandler? MessagesChanged;

    /// <inheritdoc/>
    public MessagesAvailability Availability { get; private set; } = MessagesAvailability.Unavailable;

    /// <summary>
    /// Starts following the link. Call once.
    /// </summary>
    public void Attach()
    {
        _link.ServicesReady += (sender, services) =>
        {
            if (_kind == PhoneKind.Iphone)
            {
                _ = ConnectAsync();
            }
        };
        _link.ConnectionChanged += (sender, connected) =>
        {
            if (!connected && _kind == PhoneKind.Iphone)
            {
                _ = DropAsync(MessagesAvailability.Unavailable);
            }
        };
        _notifications.EventReceived += (sender, mapEvent) => MessagesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the phone's classic Bluetooth record (MAP runs over classic RFCOMM, the link over LE).
    /// </summary>
    /// <param name="classicDeviceId">BluetoothDevice id, or null when the phone has none.</param>
    /// <param name="kind">Which phone it is: the iPhone connects with its link, an Android phone right away.</param>
    public void UsePhone(string? classicDeviceId, PhoneKind kind = PhoneKind.Iphone)
    {
        if (classicDeviceId == _classicDeviceId && kind == _kind)
        {
            return;
        }

        _ = SwitchAsync(classicDeviceId, kind);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PhoneMessage>> ListAsync(MessageFolder folder, int maxCount, CancellationToken cancellationToken = default) => RunAsync(session => session.ListAsync(folder, maxCount, cancellationToken), (IReadOnlyList<PhoneMessage>)[], cancellationToken);

    /// <inheritdoc/>
    public Task<MessageContent?> GetAsync(string handle, CancellationToken cancellationToken = default) => RunAsync(session => session.GetAsync(handle, cancellationToken), null, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> SetReadAsync(string handle, bool read, CancellationToken cancellationToken = default) => RunAsync(session => session.SetReadAsync(handle, read, cancellationToken), false, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> SendAsync(string address, string text, CancellationToken cancellationToken = default)
    {
        var handle = await RunAsync(session => session.PushAsync(address, text, cancellationToken), null, cancellationToken);

        if (handle is not null)
        {
            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }

        return handle is not null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DropAsync(MessagesAvailability.Unavailable);
        _notifications.Dispose();
    }

    private async Task ConnectAsync()
    {
        if (_classicDeviceId is null)
        {
            return;
        }

        await _sessionGate.WaitAsync();

        try
        {
            if (_session is not null)
            {
                return;
            }

            SetAvailability(MessagesAvailability.Connecting);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var device = await BluetoothDevice.FromIdAsync(_classicDeviceId).AsTask(timeout.Token);

            if (device is null)
            {
                SetAvailability(MessagesAvailability.Unavailable);
                return;
            }

            var session = new MasClient(_masLogger);
            var code = await ClassicConnectGate.RunAsync(() => session.ConnectAsync(device, timeout.Token), timeout.Token);

            if (code != ObexResponse.Success)
            {
                await session.DisposeAsync();
                _logger.LogInformation("Message access refused: 0x{Code:X2}", code);
                // With "Show Message Notifications" off the iPhone first answers Forbidden, then drops the MAS record.
                SetAvailability(code is ObexResponse.Forbidden or ObexResponse.ServiceUnavailable ? MessagesAvailability.NeedsPermission : MessagesAvailability.Unavailable);

                // The user may turn on "Show Message Notifications" while Fenpo is open.
                ScheduleRetry();
                return;
            }

            _session = session;
            await _notifications.StartAsync();
            await session.SetNotificationsAsync(true, timeout.Token);
            SetAvailability(MessagesAvailability.Ready);
            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            _logger.LogWarning(exception, "Connecting to message access failed");
            await DropUnlockedAsync(MessagesAvailability.Unavailable);
            ScheduleRetry();
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private bool Reachable => _kind == PhoneKind.Android || _link.IsConnected;

    private async Task SwitchAsync(string? classicDeviceId, PhoneKind kind)
    {
        // The other phone's session and its "messages available" end first.
        await DropAsync(MessagesAvailability.Unavailable);
        _classicDeviceId = classicDeviceId;
        _kind = kind;

        if (Reachable)
        {
            await ConnectAsync();
        }
    }

    private void ScheduleRetry()
    {
        _retry?.Cancel();
        _retry = new CancellationTokenSource();
        var token = _retry.Token;
        _ = Task.Delay(_permissionRetry, token).ContinueWith(
            _ =>
            {
                if (Reachable)
                {
                    _ = ConnectAsync();
                }
            },
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private async Task<T> RunAsync<T>(Func<MasClient, Task<T>> operation, T fallback, CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken);

        try
        {
            if (_session is null)
            {
                return fallback;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_operationTimeout);
            return await operation(_session).WaitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A timed out or broken exchange leaves the OBEX session out of step: start over.
            _logger.LogWarning(exception, "Message access operation failed; reconnecting");
            await DropUnlockedAsync(MessagesAvailability.Connecting);
            _ = Task.Run(ConnectAsync, CancellationToken.None);
            return fallback;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task DropAsync(MessagesAvailability availability)
    {
        await _sessionGate.WaitAsync();

        try
        {
            _retry?.Cancel();
            await DropUnlockedAsync(availability);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task DropUnlockedAsync(MessagesAvailability availability)
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        SetAvailability(availability);
    }

    private void SetAvailability(MessagesAvailability availability)
    {
        if (Availability == availability)
        {
            return;
        }

        Availability = availability;
        _status.Update(_kind, s => s with { MessagesAvailable = availability == MessagesAvailability.Ready });
        AvailabilityChanged?.Invoke(this, availability);
    }
}
