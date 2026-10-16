using Fenpo.App.Services;
using Fenpo.App.ViewModels;
using Fenpo.Bluetooth;
using Fenpo.Core.Api;
using Fenpo.Bluetooth.Ams;
using Fenpo.Bluetooth.Ancs;
using Fenpo.Core.Channel;
using Fenpo.Core.Media;
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
    private ScreenMirrorWindow? _mirrorWindow;

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

                // Before the channels start, so no Hello from a phone goes unseen.
                .AddSingleton<PhoneChannel>()
                .AddHostedService(sp => sp.GetRequiredService<PhoneChannel>())
                .AddFenpoBluetooth()

                // The iPhone (ANCS, AMS) and an Android phone (Fenpo channel) behind one notification and media source.
                .AddSingleton<AndroidNotificationSource>()
                .AddHostedService(sp => sp.GetRequiredService<AndroidNotificationSource>())
                .AddSingleton<AndroidMediaSource>()
                .AddHostedService(sp => sp.GetRequiredService<AndroidMediaSource>())
                .AddSingleton<INotificationSource>(sp => new CombinedNotificationSource([("i:", sp.GetRequiredService<AncsClient>()), ("a:", sp.GetRequiredService<AndroidNotificationSource>())]))
                .AddSingleton(sp => new CombinedMediaSource([sp.GetRequiredService<AmsClient>(), sp.GetRequiredService<AndroidMediaSource>()]))
                .AddSingleton<IMediaSource>(sp => sp.GetRequiredService<CombinedMediaSource>())
                .AddSingleton<FenpoMessageSource>()
                .AddHostedService(sp => sp.GetRequiredService<FenpoMessageSource>())
                .AddSingleton<Fenpo.Core.Messages.IMessageSource>(sp => new Fenpo.Core.Messages.CombinedMessageSource([sp.GetRequiredService<Fenpo.Bluetooth.Map.MapMessageSource>(), sp.GetRequiredService<FenpoMessageSource>()]))
                .AddHostedService<ActivePhoneSync>()
                .AddHostedService<PcMediaShare>()
                .AddSingleton<PcNotificationForwarder>()
                .AddHostedService<SharedFolderService>()
                .AddSingleton<Fenpo.Transport.LocalSend.LocalSendServer>()
                .AddHostedService<LocalSendBridge>()
                .AddHostedService(sp => sp.GetRequiredService<PcNotificationForwarder>())
                .AddSingleton<PcInputService>()
                .AddHostedService(sp => sp.GetRequiredService<PcInputService>())
                .AddSingleton<NotificationStore>()
                .AddSingleton<AppIconService>()
                .AddSingleton<MessageContacts>()
                .AddSingleton<PairedAppDeviceStore>()
                .AddSingleton<IPairedAppDevices>(sp => sp.GetRequiredService<PairedAppDeviceStore>())
                .AddSingleton<IBleCounters, BleCounterStore>()
                .AddSingleton(sp => PcCertificate.LoadOrCreate(Path.Combine(localFolder, "pc-certificate.pfx"), Environment.MachineName))
                .AddSingleton(new PairingTokens())
                .AddSingleton<FileTransferService>()
                .AddHostedService(sp => sp.GetRequiredService<FileTransferService>())
                .AddSingleton<ScreenMirrorService>()
                .AddHostedService(sp => sp.GetRequiredService<ScreenMirrorService>())
                .AddSingleton<CameraService>()
                .AddHostedService(sp => sp.GetRequiredService<CameraService>())
                .AddHostedService<MediaStreamRouter>()
                .AddSingleton<ControlServer>()
                .AddHostedService<TransportHostedService>()
                .AddSingleton<PhoneActions>()
                .AddHostedService(sp => sp.GetRequiredService<PhoneActions>())
                .AddSingleton<ClipboardSync>()
                .AddHostedService(sp => sp.GetRequiredService<ClipboardSync>())
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
                .AddTransient<ClipboardViewModel>()
                .AddTransient<FilesViewModel>()
                .AddTransient<SharingSettingsViewModel>()
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
    /// Shows the phone's screen in its own window and asks the phone to start, or brings the open window forward.
    /// </summary>
    public void ShowScreenMirror() => ShowScreenMirror(askPhone: true);

    // askPhone false: the phone started the stream itself (an iPhone broadcast), so the window only shows it.
    private void ShowScreenMirror(bool askPhone)
    {
        if (_mirrorWindow is not null)
        {
            _mirrorWindow.Activate();
            return;
        }

        _mirrorWindow = new ScreenMirrorWindow(GetService<ScreenMirrorService>(), GetService<ThemeService>(), GetService<LocalizationService>(), GetService<ILogger<ScreenMirrorWindow>>());
        _mirrorWindow.Closed += (sender, e) => _mirrorWindow = null;

        if (askPhone)
        {
            _mirrorWindow.Start();
        }
        else
        {
            _mirrorWindow.Show();
        }
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
        GetService<ClipboardSync>().AttachUi(dispatcher);
        GetService<PcInputService>().AttachUi(dispatcher);
        GetService<ScreenMirrorService>().StreamOpened += (sender, feed) => dispatcher.TryEnqueue(() => ShowScreenMirror(askPhone: false));
        AppInstance.GetCurrent().Activated += (sender, e) => dispatcher.TryEnqueue(() =>
        {
            // A second "fenpo --send ..." (File Explorer's right-click command) arrives here from the new process.
            if (e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch && SendArguments(NativeMethods.SplitCommandLine(launch.Arguments)) is { Count: > 0 } paths)
            {
                SendFromExplorer(paths);
                return;
            }

            ShowMainWindow();
        });
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

        if (SendArguments(arguments) is { Count: > 0 } sendPaths)
        {
            SendFromExplorer(sendPaths);
            return;
        }

        // "--sample" fills the Files and Clipboard lists for the same checks.
        if (arguments.Contains("--sample"))
        {
            GetService<FileTransferService>().ShowSamples();
            GetService<ClipboardSync>().ShowSamples();
        }

        ShowMainWindow(page);
    }

    // "--send <file> <file> ...": the files after the switch.
    private static List<string> SendArguments(IReadOnlyList<string> arguments)
    {
        var index = arguments.ToList().IndexOf("--send");
        return index < 0 ? [] : [.. arguments.Skip(index + 1).Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))];
    }

    // The Files page shows the transfer and, when it cannot start, why.
    private void SendFromExplorer(IReadOnlyList<string> paths)
    {
        ShowMainWindow("Files");
        _ = GetService<FileTransferService>().SendAsync(paths);
    }

    private void WriteCrash(string message, Exception exception) => File.AppendAllText(_crashLog, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}");
}
