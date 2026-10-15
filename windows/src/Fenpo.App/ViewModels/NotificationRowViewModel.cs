using Fenpo.App.Services;
using Fenpo.Core.Notifications;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fenpo.App.ViewModels;

/// <summary>
/// One notification row (home page, notifications page, tray).
/// </summary>
public sealed class NotificationRowViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationRowViewModel"/> class.
    /// </summary>
    /// <param name="stored">Stored notification.</param>
    /// <param name="iconPath">Cached app icon, if any.</param>
    /// <param name="time">Relative time text.</param>
    public NotificationRowViewModel(StoredNotification stored, string? iconPath, string time)
    {
        var notification = stored.Notification;
        Id = stored.Id;
        AppId = notification.AppId;
        AppName = notification.AppName;
        Title = string.IsNullOrEmpty(notification.Title) ? notification.AppName : notification.Title;
        Message = string.IsNullOrEmpty(notification.Subtitle) ? notification.Message : $"{notification.Subtitle} · {notification.Message}";
        Time = time;
        Category = notification.Category;
        CanDismiss = notification.NegativeActionLabel is not null && stored.Key is not null;
        PositiveLabel = stored.Key is null ? null : notification.PositiveActionLabel;
        AppIcon = iconPath is null ? null : new BitmapImage(new Uri(iconPath));
        AppInitial = Avatar.Initial(AppName);
        AppBrush = Avatar.Fill(AppId);
        SenderInitial = Avatar.Initial(Title);
        SenderBrush = Avatar.Fill(Title);
    }

    /// <summary>Gets the stable id.</summary>
    public string Id { get; }

    /// <summary>Gets the app bundle id.</summary>
    public string AppId { get; }

    /// <summary>Gets the app name.</summary>
    public string AppName { get; }

    /// <summary>Gets the title (sender), falling back to the app name.</summary>
    public string Title { get; }

    /// <summary>Gets the message, with the subtitle in front when there is one.</summary>
    public string Message { get; }

    /// <summary>Gets the relative time ("2 dk").</summary>
    public string Time { get; }

    /// <summary>Gets the category.</summary>
    public NotificationCategory Category { get; }

    /// <summary>Gets a value indicating whether "Dismiss" is available (ANCS negative action, current connection).</summary>
    public bool CanDismiss { get; }

    /// <summary>Gets the positive action label, if the phone offers one.</summary>
    public string? PositiveLabel { get; }

    /// <summary>Gets a value indicating whether a positive action exists.</summary>
    public bool HasPositive => PositiveLabel is not null;

    /// <summary>Gets the app icon, or null to draw the initial.</summary>
    public ImageSource? AppIcon { get; }

    /// <summary>Gets a value indicating whether an icon image exists.</summary>
    public bool HasAppIcon => AppIcon is not null;

    /// <summary>Gets the app initial for the fallback tile.</summary>
    public string AppInitial { get; }

    /// <summary>Gets the app tile color.</summary>
    public Brush AppBrush { get; }

    /// <summary>Gets the sender initial for avatars.</summary>
    public string SenderInitial { get; }

    /// <summary>Gets the avatar color.</summary>
    public Brush SenderBrush { get; }

    /// <summary>
    /// Formats a time relative to now: "şimdi", "5 dk", "3 sa", "Dün", weekday, date.
    /// </summary>
    /// <param name="when">Time to format.</param>
    /// <param name="localization">Strings.</param>
    /// <returns>Short text.</returns>
    public static string RelativeTime(DateTimeOffset when, LocalizationService localization)
    {
        var age = DateTimeOffset.Now - when;

        if (age < TimeSpan.FromMinutes(1))
        {
            return localization.GetString("Time_Now");
        }

        if (age < TimeSpan.FromHours(1))
        {
            return localization.Format("Time_Minutes", (int)age.TotalMinutes);
        }

        if (when.Date == DateTime.Today)
        {
            return when.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentUICulture);
        }

        if (when.Date == DateTime.Today.AddDays(-1))
        {
            return localization.GetString("Time_Yesterday");
        }

        return age < TimeSpan.FromDays(7)
            ? when.ToString("ddd", System.Globalization.CultureInfo.CurrentUICulture)
            : when.ToString("d MMM", System.Globalization.CultureInfo.CurrentUICulture);
    }
}
