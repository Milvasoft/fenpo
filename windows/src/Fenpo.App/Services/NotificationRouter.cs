using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Fenpo.Core.Devices;
using Fenpo.Core.Notifications;
using Fenpo.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Fenpo.App.Services;

/// <summary>
/// Routes phone notifications: keeps them in the <see cref="NotificationStore"/>, shows new ones as Windows
/// notifications with the phone's actions, removes the Windows notification when the phone removes it, and sends
/// "Clear" back to the phone. Incoming calls go to the island instead (calls service).
/// </summary>
public sealed class NotificationRouter(
    INotificationSource source,
    NotificationStore store,
    AppIconService icons,
    ISettingsStore settings,
    LocalizationService localization,
    IPhoneStatusSource status,
    ILogger<NotificationRouter> logger) : IHostedService
{
    private const string _group = "phone";

    private readonly INotificationSource _source = source;
    private readonly NotificationStore _store = store;
    private readonly AppIconService _icons = icons;
    private readonly ISettingsStore _settings = settings;
    private readonly LocalizationService _localization = localization;
    private readonly IPhoneStatusSource _status = status;
    private readonly ILogger<NotificationRouter> _logger = logger;
    private CancellationTokenSource? _dropDetached;

    /// <summary>
    /// Raised from a background thread when a click on a Windows notification should open a Fenpo page.
    /// </summary>
    public event EventHandler<string>? OpenRequested;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _source.Added += (_, notification) => _store.Add(notification);
        _source.Modified += (_, notification) => _store.Modify(notification);
        _source.Removed += (_, key) => _store.Remove(key);
        _source.Reset += (_, keyPrefix) => OnReset(keyPrefix);
        _store.Arrived += (sender, stored) => _ = ShowAsync(stored);
        _store.Dismissed += (sender, id) => _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(id, _group).AsTask();

        AppNotificationManager.Default.NotificationInvoked += (sender, args) => _ = OnInvokedAsync(args.Arguments, args.UserInput);

        try
        {
            AppNotificationManager.Default.Register();
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            // Without registration Windows notifications cannot be shown; everything else keeps working.
            _logger.LogError(exception, "Windows notifications could not be registered");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        AppNotificationManager.Default.Unregister();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends the negative ("Clear") or positive action of a stored notification to the phone.
    /// </summary>
    /// <param name="id">Stable notification id.</param>
    /// <param name="positive">True for the positive action.</param>
    /// <returns>True when the phone accepted it.</returns>
    public async Task<bool> PerformActionAsync(string id, bool positive)
    {
        if (_store.KeyOf(id) is not { } key)
        {
            _logger.LogInformation("Notification {Id} is not on the phone right now", id);
            return false;
        }

        return await _source.PerformActionAsync(key, positive);
    }

    private void OnReset(string keyPrefix)
    {
        _store.Detach(keyPrefix);

        // After a reconnect the phone re-sends what it still has as pre-existing; whatever is not back in a minute is gone.
        _dropDetached?.Cancel();
        _dropDetached = new CancellationTokenSource();
        var token = _dropDetached.Token;
        _ = Task.Delay(TimeSpan.FromMinutes(1), token).ContinueWith(_ => _store.DropDetached(), token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private async Task ShowAsync(StoredNotification stored)
    {
        var notification = stored.Notification;

        if (!_settings.Current.ShowNotifications || notification.Category == NotificationCategory.IncomingCall)
        {
            return;
        }

        // A Focus on the phone keeps the PC quiet too; the notification is still listed.
        if (_settings.Current.FocusSync && _status.Current.FocusActive)
        {
            return;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(string.IsNullOrEmpty(notification.Title) ? notification.AppName : notification.Title)
                .AddText(string.IsNullOrEmpty(notification.Subtitle) ? notification.Message : $"{notification.Subtitle}\n{notification.Message}")
                .SetAttributionText(notification.AppName)
                .SetTag(stored.Id)
                .SetGroup(_group);

            if (notification.Date is { } date)
            {
                builder.SetTimeStamp(date.LocalDateTime);
            }

            // An Android sender photo goes in the round logo spot; otherwise the app's icon.
            if (SenderPhotoPath(notification) is { } photo)
            {
                builder.SetAppLogoOverride(new Uri(photo), AppNotificationImageCrop.Circle);
            }
            else if (await _icons.GetIconPathAsync(notification.AppId) is { } icon)
            {
                builder.SetAppLogoOverride(new Uri(icon), AppNotificationImageCrop.Default);
            }

            if (notification.Actions.Count > 0)
            {
                AddPhoneActions(builder, notification, stored.Id);
            }
            else if (notification.PositiveActionLabel is { } positive)
            {
                builder.AddButton(new AppNotificationButton(positive).AddArgument("action", "positive").AddArgument("id", stored.Id));
            }

            if (notification.NegativeActionLabel is not null)
            {
                builder.AddButton(new AppNotificationButton(_localization.GetString("Notification_Dismiss")).AddArgument("action", "negative").AddArgument("id", stored.Id));
            }

            var appNotification = builder.BuildNotification();
            appNotification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(appNotification);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Showing notification {Id} from {App} failed", stored.Id, notification.AppName);
        }
    }

    // Android's own buttons: the first reply action gets a text box, the others plain buttons; Windows shows at most
    // five buttons, one of which stays for "Clear".
    private void AddPhoneActions(AppNotificationBuilder builder, PhoneNotification notification, string id)
    {
        var reply = notification.Actions.FirstOrDefault(a => a.AcceptsReply);

        if (reply is not null)
        {
            builder.AddTextBox("reply", _localization.GetString("Notification_ReplyHint"), string.Empty);
            builder.AddButton(new AppNotificationButton(reply.Title).AddArgument("action", "run").AddArgument("index", reply.Index.ToString(CultureInfo.InvariantCulture)).AddArgument("id", id).SetInputId("reply"));
        }

        foreach (var action in notification.Actions.Where(a => !a.AcceptsReply).Take(reply is null ? 4 : 3))
        {
            builder.AddButton(new AppNotificationButton(action.Title).AddArgument("action", "run").AddArgument("index", action.Index.ToString(CultureInfo.InvariantCulture)).AddArgument("id", id));
        }
    }

    private string? SenderPhotoPath(PhoneNotification notification)
    {
        if (notification.LargeIconPng is not { Length: > 0 } png)
        {
            return null;
        }

        try
        {
            var folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "people");
            var path = Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(png))[..16] + ".png");

            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(path, png);
            }

            return path;
        }
        catch (IOException exception)
        {
            _logger.LogDebug(exception, "Saving a sender photo failed");
            return null;
        }
    }

    private async Task OnInvokedAsync(IDictionary<string, string> arguments, IDictionary<string, string> userInput)
    {
        // The phone rang the PC: "Stop" takes the ringing notification away, and with it the sound.
        if (arguments.TryGetValue("ring", out _))
        {
            await AppNotificationManager.Default.RemoveByTagAsync(PhoneActions.RingTag);
            return;
        }

        // Fenpo's own notifications (received files): "Show" selects the file, a click opens their page.
        if (arguments.TryGetValue("reveal", out var reveal))
        {
            if (File.Exists(reveal))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{reveal}\"") { UseShellExecute = true });
            }

            return;
        }

        if (arguments.TryGetValue("page", out var page))
        {
            OpenRequested?.Invoke(this, page);
            return;
        }

        if (!arguments.TryGetValue("action", out var action) || !arguments.TryGetValue("id", out var id))
        {
            // A click on the notification itself, not on one of its buttons.
            OpenRequested?.Invoke(this, "Notifications");
            return;
        }

        if (action == "run")
        {
            var key = _store.KeyOf(id);
            var index = arguments.TryGetValue("index", out var text) && int.TryParse(text, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
            var reply = userInput.TryGetValue("reply", out var typed) && typed.Length > 0 ? typed : null;
            var ran = key is not null && index >= 0 && await _source.RunActionAsync(key, index, reply);
            _logger.LogInformation("Toast phone action {Index} on {Id}: {Sent}", index, id, ran.ToString(CultureInfo.InvariantCulture));
            return;
        }

        var accepted = await PerformActionAsync(id, action == "positive");
        _logger.LogInformation("Toast action {Action} on {Id}: {Accepted}", action, id, accepted.ToString(CultureInfo.InvariantCulture));
    }
}
