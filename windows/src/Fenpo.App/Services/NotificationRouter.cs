using System.Globalization;
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
    ILogger<NotificationRouter> logger) : IHostedService
{
    private const string _group = "phone";

    private readonly INotificationSource _source = source;
    private readonly NotificationStore _store = store;
    private readonly AppIconService _icons = icons;
    private readonly ISettingsStore _settings = settings;
    private readonly LocalizationService _localization = localization;
    private readonly ILogger<NotificationRouter> _logger = logger;
    private CancellationTokenSource? _dropDetached;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _source.Added += (_, notification) => _store.Add(notification);
        _source.Modified += (_, notification) => _store.Modify(notification);
        _source.Removed += (_, key) => _store.Remove(key);
        _source.Reset += (_, _) => OnReset();
        _store.Arrived += (sender, stored) => _ = ShowAsync(stored);
        _store.Dismissed += (sender, id) => _ = AppNotificationManager.Default.RemoveByTagAndGroupAsync(id, _group).AsTask();

        AppNotificationManager.Default.NotificationInvoked += (sender, args) => _ = OnInvokedAsync(args.Arguments);

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

    private void OnReset()
    {
        _store.Detach();

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

            if (await _icons.GetIconPathAsync(notification.AppId) is { } icon)
            {
                builder.SetAppLogoOverride(new Uri(icon), AppNotificationImageCrop.Default);
            }

            if (notification.PositiveActionLabel is { } positive)
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

    private async Task OnInvokedAsync(IDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("action", out var action) || !arguments.TryGetValue("id", out var id))
        {
            return;
        }

        var accepted = await PerformActionAsync(id, action == "positive");
        _logger.LogInformation("Toast action {Action} on {Id}: {Accepted}", action, id, accepted.ToString(CultureInfo.InvariantCulture));
    }
}
