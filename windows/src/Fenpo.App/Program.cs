using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Fenpo.App;

/// <summary>
/// Entry point. Fenpo runs once per user: a second launch hands its activation to the running instance and exits.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectedToRunningInstance())
        {
            return 0;
        }

        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });

        return 0;
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
