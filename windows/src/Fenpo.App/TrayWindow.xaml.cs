using Fenpo.App.Services;
using Fenpo.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Fenpo.App;

/// <summary>
/// Tray panel: a borderless window above the notification area that hides when it loses focus.
/// </summary>
public sealed partial class TrayWindow : Window
{
    private const int _width = 372;
    private const int _margin = 12;

    private DateTimeOffset _hiddenAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayWindow"/> class.
    /// </summary>
    /// <param name="viewModel">Tray view model.</param>
    /// <param name="themeService">Applies the Theme setting to this window.</param>
    public TrayWindow(TrayViewModel viewModel, ThemeService themeService)
    {
        ViewModel = viewModel;
        InitializeComponent();

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.HideFromSwitchers(hwnd);
        NativeMethods.UseRoundCorners(hwnd);

        themeService.Register(this);
        Activated += OnActivated;
        Panel.SizeChanged += (_, _) => Place();
    }

    /// <summary>
    /// Raised when the panel asks for the main window, with the page to show (null for the current one).
    /// </summary>
    public event EventHandler<string?>? OpenRequested;

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public TrayViewModel ViewModel { get; }

    /// <summary>
    /// Shows the panel, or hides it when it is open. A click on the tray icon while the panel is open first takes the
    /// focus away from it, so a panel hidden a moment ago stays hidden.
    /// </summary>
    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
            return;
        }

        if (DateTimeOffset.Now - _hiddenAt < TimeSpan.FromMilliseconds(300))
        {
            return;
        }

        Place();
        AppWindow.Show();
        Activate();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
        {
            _hiddenAt = DateTimeOffset.Now;
            AppWindow.Hide();
        }
    }

    private void Place()
    {
        var scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96d;
        var height = Panel.ActualHeight > 0 ? Panel.ActualHeight : 360;
        var size = new SizeInt32((int)(_width * scale), (int)(height * scale));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var margin = (int)(_margin * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - size.Width - margin, area.Y + area.Height - size.Height - margin, size.Width, size.Height));
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => Open("Settings");

    private void OnOpenClick(object sender, RoutedEventArgs e) => Open(null);

    private void Open(string? page)
    {
        AppWindow.Hide();
        OpenRequested?.Invoke(this, page);
    }
}
