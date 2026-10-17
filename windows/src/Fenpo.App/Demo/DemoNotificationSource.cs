using Fenpo.Core.Notifications;

namespace Fenpo.App.Demo;

/// <summary>
/// The demo phone's notifications, put straight into the store (no router runs in demo mode). Dismissing one removes it.
/// </summary>
/// <param name="store">Notification store the pages read.</param>
public sealed class DemoNotificationSource(NotificationStore store) : INotificationSource
{
    private readonly NotificationStore _store = store;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Added
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Modified
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler<string>? Removed
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler<string>? Reset
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Fills the store with the made-up notifications, as already on the phone (no toast, no ringing).
    /// </summary>
    /// <param name="localization">Strings for the action buttons.</param>
    public void Fill(Services.LocalizationService localization)
    {
        var now = DemoMode.Now;
        var index = 0;

        foreach (var notification in DemoContent.Current.Notifications.Reverse())
        {
            var date = now - notification.Ago;
            _store.Add(new PhoneNotification
            {
                Key = $"d:{index++}",
                AppId = notification.AppId,
                AppName = notification.AppName,
                Title = notification.Title,
                Message = notification.Message,
                Date = date,
                ReceivedAt = date,
                Category = notification.Category,
                Flags = NotificationFlags.PreExisting,
                NegativeActionLabel = localization.GetString("Notification_Dismiss"),
            });
        }
    }

    /// <inheritdoc/>
    public Task<bool> PerformActionAsync(string key, bool positive, CancellationToken cancellationToken = default)
    {
        _store.Remove(key);
        return Task.FromResult(true);
    }
}
