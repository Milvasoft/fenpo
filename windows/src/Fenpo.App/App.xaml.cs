using Fenpo.App.Services;
using Fenpo.App.ViewModels;
using Fenpo.Bluetooth;
using Fenpo.Core.Api;
using Fenpo.Core.Channel;
using Fenpo.Core.Notifications;
using Fenpo.Core.Pairing;
using Fenpo.Core.Settings;
using Fenpo.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Serilog;
using Windows.Storage;

namespace Fenpo.App;

/// <summary>
/// Application entry: builds the generic host (services, logging, settings), puts Fenpo in the notification area and
/// shows the main window. Closing the window keeps Fenpo running in the tray; "Quit" in the tray menu ends it.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private readonly string _crashLog;
    private MainWindow? _window;
    private TrayWindow? _trayWindow;
    private TrayIconService? _trayIcon;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    public App()
    {
        var localFolder = ApplicationData.Current.LocalFolder.Path;
        var settings = new JsonSettingsStore(Path.Combine(localFolder, "settings.json"));

        if (settings.Current.PcId is null)
        {
            settings.Update(s => s with { PcId = Guid.NewGuid().ToString() });
        }

        // Resources load with the first XAML, so the language override has to come before InitializeComponent.
        LocalizationService.ApplyAtStartup(settings.Current.Language);
        _crashLog = Path.Combine(localFolder, "crash.log");

        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            WriteCrash("InitializeComponent failed", exception);
            throw;
        }

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((_, configuration) => configuration
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(localFolder, "logs", "fenpo-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14))
            .ConfigureServices(services => services
                .AddSingleton<ISettingsStore>(settings)
                .AddSingleton<ThemeService>()
                .AddSingleton<LocalizationService>()
                .AddFenpoBluetooth()
                .AddSingleton<NotificationStore>()
                .AddSingleton<AppIconService>()
                .AddSingleton<MessageContacts>()
                .AddSingleton<PairedAppDeviceStore>()
                .AddSingleton<IPairedAppDevices>(sp => sp.GetRequiredService<PairedAppDeviceStore>())
                .AddSingleton<IBleCounters, BleCounterStore>()
                .AddSingleton(sp => PcCertificate.LoadOrCreate(Path.Combine(localFolder, "pc-certificate.pfx"), Environment.MachineName))
                .AddSingleton(new PairingTokens())
                .AddSingleton<ControlServer>()
                .AddHostedService<TransportHostedService>()
                .AddSingleton(new PcIdentity(settings.Current.PcId!, Environment.MachineName, typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0", $"Windows {Environment.OSVersion.Version}", string.Empty))
                .AddSingleton<SentMessages>()
                .AddSingleton<NotificationRouter>()
                .AddHostedService(sp => sp.GetRequiredService<NotificationRouter>())
                .AddHostedService<MediaBridge>()
                .AddHostedService<LocalApiServer>()
                .AddSingleton<CallService>()
                .AddHostedService(sp => sp.GetRequiredService<CallService>())
                .AddSingleton<ProximityLockService>()
                .AddHostedService(sp => sp.GetRequiredService<ProximityLockService>())
                .AddSingleton<MainWindow>()
                .AddSingleton<TrayViewModel>()
                .AddSingleton<TrayWindow>()
                .AddSingleton<TrayIconService>()
                .AddTransient<HomeViewModel>()
                .AddTransient<NotificationsViewModel>()
                .AddTransient<MessagesViewModel>()
                .AddTransient<CallsViewModel>()
                .AddTransient<AppFeatureViewModel>()
                .AddTransient<PairingViewModel>()
                .AddTransient<AppPairingViewModel>()
                .AddTransient<SettingsViewModel>())
            .Build();

        // Written straight to a file as well: a XAML failure can end the process before the logger flushes.
        UnhandledException += (_, args) =>
        {
            WriteCrash(args.Message, args.Exception);
            GetService<ILogger<App>>().LogError(args.Exception, "Unhandled exception: {Message}", args.Message);
        };
    }

    /// <summary>
    /// Resolves a service from the application's host.
    /// </summary>
    /// <typeparam name="T">Service type.</typeparam>
    /// <returns>The service.</returns>
    public static T GetService<T>()
        where T : notnull => ((App)Current)._host.Services.GetRequiredService<T>();

    /// <inheritdoc/>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await LaunchAsync();
        }
        catch (Exception exception)
        {
            WriteCrash("Launch failed", exception);
            throw;
        }
    }

    /// <summary>
    /// Shows the main window, optionally on a page.
    /// </summary>
    /// <param name="page">Page tag, or null to keep the current page.</param>
    public void ShowMainWindow(string? page = null)
    {
        if (_window is null)
        {
            return;
        }

        if (page is not null)
        {
            _window.Navigate(page);
        }

        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        _window.AppWindow.Show();
        _window.Activate();
    }

    /// <summary>
    /// Stops the services, removes the tray icon and ends the process.
    /// </summary>
    /// <returns>A task that completes when the services stopped.</returns>
    public async Task QuitAsync()
    {
        _trayIcon?.Dispose();
        await _host.StopAsync(TimeSpan.FromSeconds(3));
        _trayWindow?.Close();
        _window?.CloseForExit();
        Exit();
    }

    private async Task LaunchAsync()
    {
        await _host.StartAsync();
        GetService<ILogger<App>>().LogInformation("Fenpo {Version} started from {BaseDirectory}", typeof(App).Assembly.GetName().Version, AppContext.BaseDirectory);
        _window = GetService<MainWindow>();
        _trayWindow = GetService<TrayWindow>();
        _trayWindow.OpenRequested += (sender, page) => ShowMainWindow(page);

        _trayIcon = GetService<TrayIconService>();
        _trayIcon.PanelRequested += (sender, e) => _trayWindow.Toggle();
        _trayIcon.OpenRequested += (sender, e) => ShowMainWindow();
        _trayIcon.QuitRequested += (sender, e) => _ = QuitAsync();
        _trayIcon.Create();

        // A second "fenpo" (Start menu, taskbar) is redirected here by Program.Main.
        var dispatcher = _window.DispatcherQueue;
        var calls = GetService<CallService>();
        calls.AttachUi(dispatcher);

        // The Wi-Fi channel's state shows in the phone status (the "Same Wi-Fi" chip, the app-only actions).
        var tracker = GetService<Fenpo.Bluetooth.Status.PhoneStatusTracker>();
        GetService<ControlServer>().SessionChanged += (sender, change) => tracker.Update(s => s with { WifiConnected = change.Connected, AppConnected = change.Connected || s.AppConnected });
        AppInstance.GetCurrent().Activated += (sender, e) => dispatcher.TryEnqueue(() => ShowMainWindow());
        GetService<NotificationRouter>().OpenRequested += (sender, page) => dispatcher.TryEnqueue(() => ShowMainWindow(page));

        // Started with Windows: stay in the tray. Started by a notification click: the router opens the right page.
        var kind = AppInstance.GetCurrent().GetActivatedEventArgs().Kind;

        if (kind is ExtendedActivationKind.StartupTask or ExtendedActivationKind.AppNotification)
        {
            return;
        }

        // "fenpo --page Settings" opens on a given page, "--page Tray" shows only the tray panel and "--page Island" a
        // sample call on the island (used by the screenshot checks in both themes and languages).
        var arguments = Environment.GetCommandLineArgs();
        var pageIndex = Array.IndexOf(arguments, "--page");
        var page = pageIndex >= 0 && pageIndex + 1 < arguments.Length ? arguments[pageIndex + 1] : null;

        if (page == "Tray")
        {
            _trayWindow.Toggle();
            return;
        }

        if (page == "Island")
        {
            calls.ShowSample();
            return;
        }

        ShowMainWindow(page);
    }

    private void WriteCrash(string message, Exception exception) => File.AppendAllText(_crashLog, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}");
}
