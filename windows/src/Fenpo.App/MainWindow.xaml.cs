using Fenpo.App.Services;
using Fenpo.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Fenpo.App;

/// <summary>
/// Main window: custom title bar, Mica, navigation between the Fenpo pages.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> _pages = new()
    {
        ["Home"] = typeof(HomePage),
        ["Notifications"] = typeof(NotificationsPage),
        ["Messages"] = typeof(MessagesPage),
        ["Calls"] = typeof(CallsPage),
        ["Files"] = typeof(FilesPage),
        ["Clipboard"] = typeof(ClipboardPage),
        ["Camera"] = typeof(CameraPage),
        ["Settings"] = typeof(SettingsPage),
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="themeService">Applies the Theme setting to this window.</param>
    public MainWindow(ThemeService themeService)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/Fenpo.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Scale(1080), Scale(720)));

        themeService.Register(this);
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    /// <summary>
    /// Shows the page registered under <paramref name="tag"/> and selects its menu item.
    /// </summary>
    /// <param name="tag">Page tag, e.g. "Notifications".</param>
    public void Navigate(string tag)
    {
        var item = Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);

        if (item is not null)
        {
            Navigation.SelectedItem = item;
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && _pages.TryGetValue(tag, out var page) && ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
        }
    }

    private int Scale(int pixels) => (int)(pixels * (Content?.XamlRoot?.RasterizationScale ?? GetDpiScale()));

    private double GetDpiScale() => NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
}
