using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Fenpo.Core.Settings;
using Microsoft.UI.Dispatching;

namespace Fenpo.App.ViewModels;

/// <summary>
/// One app in the PC notification picker.
/// </summary>
public sealed partial class PcNotificationAppItem : ObservableObject
{
    private readonly Action<PcNotificationAppItem>? _changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PcNotificationAppItem"/> class.
    /// </summary>
    /// <param name="app">The app.</param>
    /// <param name="changed">Called when the user picks or unpicks it.</param>
    public PcNotificationAppItem(PcNotificationApp app, Action<PcNotificationAppItem> changed)
    {
        Id = app.Id;
        Name = app.Name;
        Send = app.Send;
        _changed = changed;
    }

    /// <summary>Gets the app user model id.</summary>
    public string Id { get; }

    /// <summary>Gets the app's name.</summary>
    public string Name { get; }

    /// <summary>Gets or sets a value indicating whether its notifications go to the phone.</summary>
    [ObservableProperty]
    public partial bool Send { get; set; }

    // Not yet set while the constructor copies the saved choice.
    partial void OnSendChanged(bool value) => _changed?.Invoke(this);
}

/// <summary>
/// One shared folder in Settings.
/// </summary>
/// <param name="Name">Name the phone shows.</param>
/// <param name="Path">Path on the PC.</param>
public sealed record SharedFolderItem(string Name, string Path);

/// <summary>
/// Settings › Sharing with the phone: the PC's notifications on the phone, the PC folders the phone can browse, and
/// whether LocalSend apps see this PC.
/// </summary>
public sealed partial class SharingSettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly PcNotificationForwarder _forwarder;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _loading = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharingSettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">Settings store.</param>
    /// <param name="forwarder">Reads the PC's notifications.</param>
    /// <param name="localization">Strings.</param>
    public SharingSettingsViewModel(ISettingsStore settings, PcNotificationForwarder forwarder, LocalizationService localization)
    {
        _settings = settings;
        _forwarder = forwarder;
        _localization = localization;
        var current = settings.Current;
        PcNotificationsToPhone = current.PcNotificationsToPhone && forwarder.HasAccess;
        LocalSendVisible = current.LocalSendVisible;
        Apply(current);
        _settings.Changed += OnSettingsChanged;
        _loading = false;
    }

    /// <summary>Gets the apps that showed notifications on the PC.</summary>
    public ObservableCollection<PcNotificationAppItem> Apps { get; } = [];

    /// <summary>Gets the shared folders.</summary>
    public ObservableCollection<SharedFolderItem> Folders { get; } = [];

    /// <summary>Gets or sets a value indicating whether the PC's notifications go to the phone.</summary>
    [ObservableProperty]
    public partial bool PcNotificationsToPhone { get; set; }

    /// <summary>Gets or sets why the PC's notifications cannot be read, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotificationsNotice))]
    public partial string NotificationsNotice { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether <see cref="NotificationsNotice"/> shows.</summary>
    public bool HasNotificationsNotice => NotificationsNotice.Length > 0;

    /// <summary>Gets or sets a value indicating whether no app showed a notification yet.</summary>
    [ObservableProperty]
    public partial bool NoApps { get; set; }

    /// <summary>Gets or sets a value indicating whether no folder is shared.</summary>
    [ObservableProperty]
    public partial bool NoFolders { get; set; }

    /// <summary>Gets or sets a value indicating whether LocalSend apps see this PC.</summary>
    [ObservableProperty]
    public partial bool LocalSendVisible { get; set; }

    /// <inheritdoc/>
    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    /// <summary>
    /// Shares a folder with the phone under its own name, made unique among the shared folders.
    /// </summary>
    /// <param name="path">Folder on the PC.</param>
    public void AddFolder(string path)
    {
        var folders = _settings.Current.SharedFolders;

        if (folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        name = string.IsNullOrEmpty(name) ? path.TrimEnd(':', Path.DirectorySeparatorChar) : name;
        var unique = name;

        for (var index = 2; folders.Any(f => string.Equals(f.Name, unique, StringComparison.OrdinalIgnoreCase)); index++)
        {
            unique = $"{name} ({index})";
        }

        _settings.Update(s => s with { SharedFolders = [.. s.SharedFolders, new SharedFolder(unique, path, false)] });
    }

    [RelayCommand]
    private void RemoveFolder(SharedFolderItem folder) => _settings.Update(s => s with { SharedFolders = [.. s.SharedFolders.Where(f => f.Path != folder.Path)] });

    async partial void OnPcNotificationsToPhoneChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        NotificationsNotice = string.Empty;

        // Windows asks its user once; without access there is nothing to send.
        if (value && !_forwarder.HasAccess && !await _forwarder.RequestAccessAsync())
        {
            NotificationsNotice = _localization.GetString("Sharing_NotificationsDenied");
            PcNotificationsToPhone = false;
            return;
        }

        _settings.Update(s => s with { PcNotificationsToPhone = value });
    }

    partial void OnLocalSendVisibleChanged(bool value)
    {
        if (!_loading)
        {
            _settings.Update(s => s with { LocalSendVisible = value });
        }
    }

    private void OnAppChanged(PcNotificationAppItem item) => _settings.Update(s => s with
    {
        PcNotificationApps = [.. s.PcNotificationApps.Select(a => string.Equals(a.Id, item.Id, StringComparison.OrdinalIgnoreCase) ? a with { Send = item.Send } : a)],
    });

    private void OnSettingsChanged(object? sender, AppSettings current) => _dispatcher.TryEnqueue(() => Apply(current));

    private void Apply(AppSettings current)
    {
        // New apps come in while the page is open; existing rows keep their state.
        foreach (var app in current.PcNotificationApps.Where(a => Apps.All(i => !string.Equals(i.Id, a.Id, StringComparison.OrdinalIgnoreCase))).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Apps.Add(new PcNotificationAppItem(app, OnAppChanged));
        }

        NoApps = Apps.Count == 0;

        if (!Folders.Select(f => f.Path).SequenceEqual(current.SharedFolders.Select(f => f.Path)))
        {
            Folders.Clear();

            foreach (var folder in current.SharedFolders)
            {
                Folders.Add(new SharedFolderItem(folder.Name, folder.Path));
            }
        }

        NoFolders = Folders.Count == 0;
    }
}
