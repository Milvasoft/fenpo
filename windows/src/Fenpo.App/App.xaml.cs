using Fenpo.App.Services;
using Fenpo.App.ViewModels;
using Fenpo.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using Windows.Storage;

namespace Fenpo.App;

/// <summary>
/// Application entry: builds the generic host (services, logging, settings) and shows the main window.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private readonly string _crashLog;
    private MainWindow? _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    public App()
    {
        var localFolder = ApplicationData.Current.LocalFolder.Path;
        var settings = new JsonSettingsStore(Path.Combine(localFolder, "settings.json"));

        // Resources load with the first XAML, so the language override has to come before InitializeComponent.
        LocalizationService.ApplyAtStartup(settings.Current.Language);        _crashLog = Path.Combine(localFolder, "crash.log");

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
                .AddSingleton<MainWindow>()
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

    private async Task LaunchAsync()
    {
        await _host.StartAsync();
        GetService<ILogger<App>>().LogInformation("Fenpo {Version} started from {BaseDirectory}", typeof(App).Assembly.GetName().Version, AppContext.BaseDirectory);
        _window = GetService<MainWindow>();

        // "fenpo --page Settings" opens on a given page (used by the screenshot checks in both themes and languages).
        var arguments = Environment.GetCommandLineArgs();
        var pageIndex = Array.IndexOf(arguments, "--page");

        if (pageIndex >= 0 && pageIndex + 1 < arguments.Length)
        {
            _window.Navigate(arguments[pageIndex + 1]);
        }

        _window.Activate();
    }

    private void WriteCrash(string message, Exception exception) => File.AppendAllText(_crashLog, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}");
}
