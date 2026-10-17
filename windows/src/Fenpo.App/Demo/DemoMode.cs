using Fenpo.App.Services;
using Fenpo.Core.Calls;
using Fenpo.Core.Contacts;
using Fenpo.Core.Devices;
using Fenpo.Core.Media;
using Fenpo.Core.Messages;
using Fenpo.Core.Notifications;
using Fenpo.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Windows.Storage;
using TransferFile = Fenpo.Protocol.V1.FileInfo;

namespace Fenpo.App.Demo;

/// <summary>
/// "fenpo --demo": a made-up iPhone with made-up notifications, messages, calls, music, files and clipboard, for the
/// store screenshots. The real phone and the user's data stay out of it: no hosted service starts (no Bluetooth, no
/// network, no PC clipboard) and every file Fenpo keeps goes to an emptied folder of its own (<see cref="AppFolders"/>).
/// </summary>
public static class DemoMode
{
    private static readonly DateTimeOffset _started = DateTimeOffset.Now;

    /// <summary>
    /// Gets a value indicating whether Fenpo was started with <c>--demo</c>.
    /// </summary>
    public static bool IsOn { get; } = Environment.GetCommandLineArgs().Contains("--demo");

    /// <summary>
    /// Gets the demo's clock: 9:41 today when Fenpo started, as product pictures show it, running on from there. The
    /// made-up times and "2 min ago" are measured from it, so the pictures look the same at any hour.
    /// </summary>
    public static DateTimeOffset Now => new DateTimeOffset(DateTime.Today.AddHours(9).AddMinutes(41)) + (DateTimeOffset.Now - _started);

    /// <summary>
    /// Empties the demo folder and writes its settings. Theme and language come from <c>--theme Light|Dark</c> and
    /// <c>--language tr-TR|en-US</c> (the screenshot script), otherwise from the real settings; nothing else is taken over.
    /// </summary>
    public static void PrepareFolder()
    {
        var real = new JsonSettingsStore(Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json")).Current;
        var arguments = Environment.GetCommandLineArgs();
        string? Argument(string name) => Array.IndexOf(arguments, name) is var index and >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;

        if (Directory.Exists(AppFolders.Local))
        {
            Directory.Delete(AppFolders.Local, recursive: true);
        }

        new JsonSettingsStore(Path.Combine(AppFolders.Local, "settings.json")).Update(_ => new AppSettings
        {
            Theme = Enum.TryParse<AppTheme>(Argument("--theme"), out var theme) ? theme : real.Theme,
            Language = Argument("--language") ?? real.Language,
            SetupCompleted = true,
            ActivePhone = PhoneKind.Iphone,
        });
    }

    /// <summary>
    /// Puts the demo sources behind the phone interfaces and removes every hosted service, so nothing is started.
    /// </summary>
    /// <param name="services">Service collection, after Fenpo's own registrations.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddFenpoDemo(this IServiceCollection services)
    {
        services.RemoveAll<IHostedService>();
        return services
            .AddSingleton<IPhoneStatusSource, DemoPhoneStatusSource>()
            .AddSingleton<DemoNotificationSource>()
            .AddSingleton<INotificationSource>(sp => sp.GetRequiredService<DemoNotificationSource>())
            .AddSingleton<IMediaSource, DemoMediaSource>()
            .AddSingleton<DemoMessageSource>()
            .AddSingleton<IMessageSource>(sp => sp.GetRequiredService<DemoMessageSource>())
            .AddSingleton<IContactSource>(sp => sp.GetRequiredService<DemoMessageSource>());
    }

    /// <summary>
    /// Fills the lists the pages show: notifications, recent calls, file transfers and the clipboard history.
    /// </summary>
    /// <param name="services">The app's services.</param>
    public static void Fill(IServiceProvider services)
    {
        var content = DemoContent.Current;
        var now = Now;
        services.GetRequiredService<ISettingsStore>().Update(s => s with { IncomingFolder = content.IncomingFolder });
        services.GetRequiredService<DemoNotificationSource>().Fill(services.GetRequiredService<LocalizationService>());

        var calls = services.GetRequiredService<CallService>().Log;

        foreach (var (name, kind, ago) in content.Calls)
        {
            if (kind == CallKind.Missed)
            {
                calls.Missed(name, now - ago);
            }
            else
            {
                calls.Ringing(name, now - ago);
            }
        }

        services.GetRequiredService<FileTransferService>().Show([.. content.Transfers.Select((demo, index) =>
        {
            var transfer = new FileTransfer($"demo-{index}", "demo", demo.Incoming, [new TransferFile { Name = demo.Name, Size = demo.Size }], []) { StartedAt = now - demo.Ago };
            transfer.SetState(demo.Done < demo.Size ? FileTransferState.InProgress : FileTransferState.Completed);
            transfer.SetProgress(demo.Done);
            return transfer;
        })]);

        services.GetRequiredService<ClipboardSync>().Show(content.Clips.Reverse().Select(clip => new ClipboardEntry(clip.Text, null, clip.FromPhone, now - clip.Ago)));
    }
}
