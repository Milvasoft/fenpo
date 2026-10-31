using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Fenpo.App;

/// <summary>
/// Entry point. Fenpo runs once per user: a second launch hands its activation to the running instance and exits.
/// </summary>
public static class Program
{
    // Each installation (Store, GitHub, a development build) is its own package with its own instance key, but two
    // copies would share the Bluetooth adapter: both advertise the Fenpo service and both connect to the phone, which
    // then talks to whichever answers. Full-trust packages share the session's named objects, so a mutex spans them.
    private const string _anyCopyMutexName = @"Local\Fenpo.AnyCopy";

    private static Mutex? _anyCopy;

    [STAThread]
    private static int Main()
    {
        // Started as a plain exe (a search result or Explorer pointing at Fenpo.exe) there is no package identity, and
        // every Windows App SDK class fails with REGDB_E_CLASSNOTREG. Start the package through its alias instead.
        if (!NativeMethods.HasPackageIdentity())
        {
            RelaunchAsPackage();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectedToRunningInstance())
        {
            return 0;
        }

        _anyCopy = new Mutex(true, _anyCopyMutexName, out var first);

        if (!first)
        {
            var loader = new ResourceLoader();
            _ = NativeMethods.MessageBox(0, loader.GetString("AnotherCopy_Text"), loader.GetString("AnotherCopy_Title"), NativeMethods.MessageBoxIconInformation);
            return 0;
        }

        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });

        return 0;
    }

    private static void RelaunchAsPackage()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "fenpo.exe");

        if (!File.Exists(alias))
        {
            return;
        }

        var start = new ProcessStartInfo(alias) { UseShellExecute = false };

        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        Process.Start(start)?.Dispose();
    }

    private static bool RedirectedToRunningInstance()
    {
        var main = AppInstance.FindOrRegisterForKey("main");

        if (main.IsCurrent)
        {
            return false;
        }

        // Redirect on a worker thread: the call needs a COM apartment that is not this blocked STA.
        var arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        Task.Run(() => main.RedirectActivationToAsync(arguments).AsTask()).Wait();
        return true;
    }
}
