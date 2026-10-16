using System.Collections.Concurrent;
using Fenpo.Core.Notifications;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Category = Fenpo.Core.Notifications.NotificationCategory;
using PhoneNotification = Fenpo.Core.Notifications.PhoneNotification;
using WireCategory = Fenpo.Protocol.V1.NotificationCategory;
using WireNotification = Fenpo.Protocol.V1.PhoneNotification;

namespace Fenpo.App.Services;

/// <summary>
/// Notifications an Android phone mirrors over the Fenpo channel (CAPABILITY_NOTIFICATION_MIRROR, notification.proto),
/// as an <see cref="INotificationSource"/> next to ANCS. Order across both channels, the snapshot after each Hello and
/// removed keys follow <see cref="NotificationMirrorState"/>; app icons are asked for once and cached.
/// </summary>
public sealed class AndroidNotificationSource(PhoneChannel channel, AppIconService icons, LocalizationService localization, ILogger<AndroidNotificationSource> logger) : INotificationSource, IHostedService
{
    private static readonly TimeSpan _iconWait = TimeSpan.FromSeconds(1.5);

    // Words phone apps put on call buttons, to tell Answer from Decline when the order is not known.
    private static readonly string[] _answerWords = ["answer", "accept", "yanıtla", "cevapla", "kabul"];
    private static readonly string[] _declineWords = ["decline", "reject", "hang up", "end", "reddet", "kapat", "sonlandır"];

    private readonly PhoneChannel _channel = channel;
    private readonly AppIconService _icons = icons;
    private readonly LocalizationService _localization = localization;
    private readonly ILogger<AndroidNotificationSource> _logger = logger;
    private readonly NotificationMirrorState _state = new();
    private readonly ConcurrentDictionary<string, WireNotification> _current = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _iconRequests = new();
    private readonly SemaphoreSlim _order = new(1, 1);
    private bool _connected;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Added;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Modified;

    /// <inheritdoc/>
    public event EventHandler<string>? Removed;

    /// <inheritdoc/>
    public event EventHandler<string>? Reset;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.HelloReceived += (sender, hello) =>
        {
            if (hello.Envelope.Hello.Platform == DevicePlatform.Android)
            {
                _connected = true;
                _state.BeginSnapshot();
            }
        };
        _channel.Changed += (sender, e) => OnChannelChanged();
        _channel.MessageReceived += (sender, message) => _ = OnMessageAsync(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task<bool> PerformActionAsync(string key, bool positive, CancellationToken cancellationToken = default)
    {
        if (!_current.TryGetValue(key, out var notification))
        {
            return false;
        }

        // A call is answered or declined with its own buttons; anything else is cleared or gets its first action.
        if (notification.Category == WireCategory.IncomingCall && CallAction(notification, positive) is { } callAction)
        {
            return await RunActionAsync(key, (int)callAction.Index, null, cancellationToken);
        }

        if (!positive)
        {
            return notification.Dismissible && await _channel.SendToAndroidAsync(new Envelope { DismissNotification = new DismissNotification { Key = key } }, cancellationToken);
        }

        return notification.Actions.FirstOrDefault(a => !a.AcceptsReply) is { } first && await RunActionAsync(key, (int)first.Index, null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> RunActionAsync(string key, int index, string? reply, CancellationToken cancellationToken = default)
    {
        var sent = await _channel.SendToAndroidAsync(new Envelope { RunNotificationAction = new RunNotificationAction { Key = key, ActionIndex = (uint)index, ReplyText = reply ?? string.Empty } }, cancellationToken);
        _logger.LogInformation("Notification action {Index} ({Reply}) sent: {Sent}", index, reply is null ? "no reply" : "reply", sent);
        return sent;
    }

    private static NotificationAction? CallAction(WireNotification notification, bool answer)
    {
        var words = answer ? _answerWords : _declineWords;
        var named = notification.Actions.FirstOrDefault(a => words.Any(w => a.Title.Contains(w, StringComparison.OrdinalIgnoreCase)));

        // CallStyle puts Decline first and Answer last.
        return named ?? (answer ? notification.Actions.LastOrDefault() : notification.Actions.FirstOrDefault());
    }

    private static Category ToCategory(WireCategory category) => category switch
    {
        WireCategory.IncomingCall => Category.IncomingCall,
        WireCategory.MissedCall => Category.MissedCall,
        WireCategory.Voicemail => Category.Voicemail,
        WireCategory.Social => Category.Social,
        WireCategory.Schedule => Category.Schedule,
        WireCategory.Email => Category.Email,
        WireCategory.News => Category.News,
        WireCategory.HealthAndFitness => Category.HealthAndFitness,
        WireCategory.BusinessAndFinance => Category.BusinessAndFinance,
        WireCategory.Location => Category.Location,
        WireCategory.Entertainment => Category.Entertainment,
        _ => Category.Other,
    };

    private void OnChannelChanged()
    {
        var connected = _channel.AndroidConnected;

        if (_connected && !connected)
        {
            // Like an ANCS disconnect: items stay a minute and come back with the next snapshot.
            _connected = false;
            _state.Disconnect();
            _current.Clear();
            Reset?.Invoke(this, string.Empty);
        }
    }

    private async Task OnMessageAsync(ChannelMessage message)
    {
        // Messages about notifications are applied one at a time, in arrival order; seq sorts out the rest.
        await _order.WaitAsync();

        try
        {
            switch (message.Envelope.BodyCase)
            {
                case Envelope.BodyOneofCase.PhoneNotification:
                    await OnPostedAsync(message.Envelope.PhoneNotification);
                    break;
                case Envelope.BodyOneofCase.NotificationRemoved:
                    OnRemoved(message.Envelope.NotificationRemoved);
                    break;
                case Envelope.BodyOneofCase.NotificationSnapshotDone:
                    var gone = _state.EndSnapshot();

                    foreach (var key in gone)
                    {
                        _current.TryRemove(key, out _);
                        Removed?.Invoke(this, key);
                    }

                    _logger.LogInformation("Android notification snapshot from {Phone}: {Count} on the phone, {Gone} gone", message.Device.Name, _state.Shown.Count, gone.Count);
                    break;
                case Envelope.BodyOneofCase.AppIcon:
                    OnIcon(message.Envelope.AppIcon);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Handling {Body} from {Phone} failed", message.Envelope.BodyCase, message.Device.Name);
        }
        finally
        {
            _order.Release();
        }
    }

    private async Task OnPostedAsync(WireNotification wire)
    {
        var decision = _state.OnPosted(wire.Key, wire.Seq, wire.PreExisting, DateTimeOffset.Now);

        if (decision == MirrorDecision.Ignore)
        {
            return;
        }

        _current[wire.Key] = wire;
        await EnsureIconAsync(wire.AppId);
        var notification = ToNotification(wire);

        if (decision == MirrorDecision.Add)
        {
            _logger.LogInformation("Android notification from {App} ({Category}{Snapshot})", wire.AppId, wire.Category, wire.PreExisting ? ", snapshot" : string.Empty);
            Added?.Invoke(this, notification);
        }
        else
        {
            Modified?.Invoke(this, notification);
        }
    }

    private void OnRemoved(NotificationRemoved removed)
    {
        if (_state.OnRemoved(removed.Key, removed.Seq, DateTimeOffset.Now))
        {
            _current.TryRemove(removed.Key, out _);
            Removed?.Invoke(this, removed.Key);
        }
    }

    // The first notification of an app waits a moment for its icon so the Windows notification shows it.
    private async Task EnsureIconAsync(string appId)
    {
        if (string.IsNullOrEmpty(appId) || _icons.GetCachedIconPath(appId) is not null)
        {
            return;
        }

        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_iconRequests.TryAdd(appId, waiter))
        {
            await _channel.SendToAndroidAsync(new Envelope { AppIconRequest = new AppIconRequest { AppId = appId } });
        }
        else
        {
            waiter = _iconRequests[appId];
        }

        // The order lock is held: a late icon must not hold up the next notification for long.
        await Task.WhenAny(waiter.Task, Task.Delay(_iconWait));
    }

    private void OnIcon(AppIcon icon)
    {
        if (icon.Png.IsEmpty)
        {
            return;
        }

        _icons.Save(icon.AppId, icon.Png.ToByteArray());

        if (_iconRequests.TryRemove(icon.AppId, out var waiter))
        {
            waiter.TrySetResult();
        }
    }

    private PhoneNotification ToNotification(WireNotification wire)
    {
        var message = wire.ContentHidden
            ? _localization.GetString("Notification_ContentHidden")
            : wire.Text.Length > 0 ? wire.Text : wire.Messages.LastOrDefault()?.Text ?? string.Empty;
        var flags = NotificationFlags.None;

        if (wire.Silent)
        {
            flags |= NotificationFlags.Silent;
        }

        if (wire.PreExisting)
        {
            flags |= NotificationFlags.PreExisting;
        }

        var positive = wire.Actions.FirstOrDefault(a => !a.AcceptsReply)?.Title;

        return new PhoneNotification
        {
            Key = wire.Key,
            AppId = wire.AppId,
            AppName = wire.AppName,
            Title = wire.Title,
            Subtitle = wire.ConversationTitle,
            Message = message,
            Date = wire.PostedAt?.ToDateTimeOffset().ToLocalTime(),
            Category = ToCategory(wire.Category),
            Flags = flags,
            PositiveActionLabel = wire.Category == WireCategory.IncomingCall ? CallAction(wire, answer: true)?.Title : positive,
            NegativeActionLabel = wire.Dismissible || wire.Category == WireCategory.IncomingCall ? _localization.GetString("Notification_Dismiss") : null,
            Actions = [.. wire.Actions.Select(a => new NotificationButton((int)a.Index, a.Title, a.AcceptsReply))],
            LargeIconPng = wire.LargeIconPng.IsEmpty ? null : wire.LargeIconPng.ToByteArray(),
        };
    }
}
