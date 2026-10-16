using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Fenpo.Core.Notifications;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Notifications grouped by app.
/// </summary>
/// <param name="AppName">App name.</param>
/// <param name="AppIcon">Icon, if cached.</param>
/// <param name="AppInitial">Fallback initial.</param>
/// <param name="AppBrush">Fallback tile color.</param>
/// <param name="CountText">"2 bildirim".</param>
/// <param name="Rows">Notifications of the app, newest first.</param>
public sealed record NotificationGroupViewModel(string AppName, ImageSource? AppIcon, string AppInitial, Brush AppBrush, string CountText, IReadOnlyList<NotificationRowViewModel> Rows)
{
    /// <summary>Gets a value indicating whether an icon image exists.</summary>
    public bool HasAppIcon => AppIcon is not null;
}

/// <summary>
/// Notifications page: filter, groups by app, dismiss one or all.
/// </summary>
public sealed partial class NotificationsViewModel : ObservableObject, IDisposable
{
    private readonly NotificationStore _store;
    private readonly NotificationRouter _router;
    private readonly AppIconService _icons;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationsViewModel"/> class.
    /// </summary>
    /// <param name="store">Notification store.</param>
    /// <param name="router">Sends actions to the phone.</param>
    /// <param name="icons">App icons.</param>
    /// <param name="localization">Strings.</param>
    public NotificationsViewModel(NotificationStore store, NotificationRouter router, AppIconService icons, LocalizationService localization)
    {
        _store = store;
        _router = router;
        _icons = icons;
        _localization = localization;
        _store.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Gets the groups to show.</summary>
    public ObservableCollection<NotificationGroupViewModel> Groups { get; } = [];

    /// <summary>Gets or sets the filter: 0 all, 1 messages, 2 calls, 3 other.</summary>
    [ObservableProperty]
    public partial int FilterIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether the list is empty.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <inheritdoc/>
    public void Dispose() => _store.Changed -= OnChanged;

    partial void OnFilterIndexChanged(int value) => Refresh();

    [RelayCommand]
    private Task DismissAsync(string id) => _router.PerformActionAsync(id, positive: false);

    [RelayCommand]
    private Task PositiveAsync(string id) => _router.PerformActionAsync(id, positive: true);

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        foreach (var row in Groups.SelectMany(g => g.Rows).Where(r => r.CanDismiss).ToList())
        {
            await _router.PerformActionAsync(row.Id, positive: false);
        }
    }

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Refresh);

    private void Refresh()
    {
        var items = _store.Items.Where(i => Matches(i.Notification.Category)).ToList();
        Groups.Clear();

        foreach (var group in items.GroupBy(i => i.Notification.AppId).OrderByDescending(g => g.Max(i => i.Notification.Date ?? i.Notification.ReceivedAt)))
        {
            var icon = _icons.GetCachedIconPath(group.Key);
            var rows = group.Select(i => new NotificationRowViewModel(i, icon, NotificationRowViewModel.RelativeTime(i.Notification.Date ?? i.Notification.ReceivedAt, _localization))).ToList();
            var first = rows[0];
            Groups.Add(new NotificationGroupViewModel(first.AppName, first.AppIcon, first.AppInitial, first.AppBrush, _localization.Format("Notifications_Count", rows.Count), rows));
        }

        IsEmpty = Groups.Count == 0;
    }

    private bool Matches(NotificationCategory category) => FilterIndex switch
    {
        1 => category is NotificationCategory.Social or NotificationCategory.Email,
        2 => category is NotificationCategory.IncomingCall or NotificationCategory.MissedCall or NotificationCategory.Voicemail,
        3 => category is not (NotificationCategory.Social or NotificationCategory.Email or NotificationCategory.IncomingCall or NotificationCategory.MissedCall or NotificationCategory.Voicemail),
        _ => true,
    };
}
