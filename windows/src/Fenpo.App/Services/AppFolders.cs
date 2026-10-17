using Fenpo.App.Demo;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// Where Fenpo keeps its files (settings, logs, recent calls, sent messages, icons).
/// </summary>
public static class AppFolders
{
    /// <summary>
    /// Gets the package's local folder, or in demo mode a folder of its own inside it, so the demo neither shows nor
    /// changes what the real phone left there.
    /// </summary>
    public static string Local { get; } = DemoMode.IsOn
        ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "demo")
        : ApplicationData.Current.LocalFolder.Path;
}
