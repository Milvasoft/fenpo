using System.Collections.Concurrent;
using Fenpo.Core.Settings;
using Fenpo.Protocol.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Fenpo.App.Services;

/// <summary>
/// The PC's notifications on the phone (pc_notification.proto): reads Windows' notifications with its user's permission
/// and sends those of the picked apps to the phone in use, and their removal. Every app that shows a notification is
/// remembered so it can be picked in Settings. Fenpo's own notifications (the phone's, mirrored) are never sent back.
/// </summary>
public sealed class PcNotificationForwarder(PhoneChannel channel, ISettingsStore settings, ILogger<PcNotificationForwarder> logger) : IHostedService
{
    // The listener's change event does not reach desktop apps; Windows' list is read this often instead.
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(2);

    // On BLE the sealed message must stay under 4096 bytes.
    private const int _bleTextLimit = 1500;

    private readonly PhoneChannel _channel = channel;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<PcNotificationForwarder> _logger = logger;
    private readonly ConcurrentDictionary<string, byte[]> _icons = new();
    private readonly CancellationTokenSource _stopping = new();
    private HashSet<uint> _known = [];
    private HashSet<uint> _sent = [];
    private bool _primed;
    private string _ownId = string.Empty;

    /// <summary>
    /// Gets a value indicating whether Windows lets Fenpo read the PC's notifications.
    /// </summary>
    public bool HasAccess => UserNotificationListener.Current.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed;

    /// <summary>
    /// Asks Windows for access to the PC's notifications. Call on the UI thread; Windows may show its own prompt.
    /// </summary>
    /// <returns>True when allowed.</returns>
    public async Task<bool> RequestAccessAsync()
    {
        var status = await UserNotificationListener.Current.RequestAccessAsync();
        _logger.LogInformation("Access to the PC's notifications: {Status}", status);
        return status == UserNotificationListenerAccessStatus.Allowed;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ownId = Windows.ApplicationModel.AppInfo.Current.AppUserModelId;
        _ = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (HasAccess)
                {
                    await ReadAsync();
                }
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Reading the PC's notifications failed");
            }

            try
            {
                await Task.Delay(_interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadAsync()
    {
        var notifications = (await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast))
            .Where(n => n.AppInfo?.AppUserModelId is { } id && id != _ownId)
            .ToList();
        Remember(notifications);

        var current = notifications.Select(n => n.Id).ToHashSet();
        var phoneTakes = _settings.Current.PcNotificationsToPhone && _channel.IsConnected && _channel.Supports(Capability.PcNotifications);

        // What was already there when Fenpo started (or the phone came) is not news.
        if (!_primed || !phoneTakes)
        {
            _known = current;
            _sent.Clear();
            _primed = true;
            return;
        }

        var picked = _settings.Current.PcNotificationApps.Where(a => a.Send).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var notification in notifications.Where(n => !_known.Contains(n.Id) && picked.Contains(n.AppInfo.AppUserModelId)))
        {
            if (await SendAsync(notification))
            {
                _sent.Add(notification.Id);
            }
        }

        foreach (var gone in _sent.Where(id => !current.Contains(id)).ToList())
        {
            await _channel.SendAsync(new Envelope { PcNotificationRemoved = new PcNotificationRemoved { Id = gone.ToString(System.Globalization.CultureInfo.InvariantCulture) } });
            _sent.Remove(gone);
        }

        _known = current;
    }

    private async Task<bool> SendAsync(UserNotification notification)
    {
        var texts = notification.Notification.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
        var overWifi = _channel.WifiConnected;
        var text = string.Join(Environment.NewLine, texts.Skip(1));

        if (!overWifi && text.Length > _bleTextLimit)
        {
            text = text[.._bleTextLimit];
        }

        var message = new PcNotification
        {
            Id = notification.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AppId = notification.AppInfo.AppUserModelId,
            AppName = notification.AppInfo.DisplayInfo?.DisplayName ?? string.Empty,
            Title = texts.FirstOrDefault() ?? string.Empty,
            Text = text,
            PostedAt = Timestamp.FromDateTimeOffset(notification.CreationTime),
        };

        if (overWifi && await IconAsync(notification) is { Length: > 0 } icon)
        {
            message.AppIconPng = ByteString.CopyFrom(icon);
        }

        var sent = await _channel.SendAsync(new Envelope { PcNotification = message });
        _logger.LogInformation("PC notification from {App} to the phone: {Sent}", message.AppId, sent);
        return sent;
    }

    // The app's logo at 64x64, once per app.
    private async Task<byte[]?> IconAsync(UserNotification notification)
    {
        var id = notification.AppInfo.AppUserModelId;

        if (_icons.TryGetValue(id, out var cached))
        {
            return cached;
        }

        try
        {
            var logo = notification.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            using var stream = await logo.OpenReadAsync();
            var bytes = new byte[stream.Size];
            using var reader = new Windows.Storage.Streams.DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            _icons[id] = bytes;
            return bytes;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            _icons[id] = [];
            return null;
        }
    }

    // Every app that shows a notification can be picked in Settings; new ones start unpicked.
    private void Remember(List<UserNotification> notifications)
    {
        var known = _settings.Current.PcNotificationApps;
        var added = notifications
            .Select(n => n.AppInfo)
            .Where(app => known.All(k => !string.Equals(k.Id, app.AppUserModelId, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(app => app.AppUserModelId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PcNotificationApp(group.Key, group.First().DisplayInfo?.DisplayName ?? group.Key, false))
            .ToList();

        if (added.Count > 0)
        {
            _settings.Update(s => s with { PcNotificationApps = [.. s.PcNotificationApps, .. added] });
        }
    }
}
