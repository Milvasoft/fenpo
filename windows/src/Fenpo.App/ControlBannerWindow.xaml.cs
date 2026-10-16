using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Fenpo.App;

/// <summary>
/// The banner at the top center while the PC's keyboard and mouse go to the phone. Shown without taking the focus.
/// </summary>
public sealed partial class ControlBannerWindow : Window
{
    private const int _width = 560;
    private const int _height = 44;
    private const int _top = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlBannerWindow"/> class.
    /// </summary>
    public ControlBannerWindow()
    {
        InitializeComponent();
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        NativeMethods.HideFromSwitchers(WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Shows the banner with a text.
    /// </summary>
    /// <param name="text">What to say.</param>
    public void ShowText(string text)
    {
        Text.Text = text;
        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96d;
        var size = new SizeInt32((int)(_width * scale), (int)(_height * scale));
        var area = DisplayArea.Primary.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(area.X + ((area.Width - size.Width) / 2), area.Y + (int)(_top * scale), size.Width, size.Height));
        AppWindow.Show(activateWindow: false);
        NativeMethods.RemoveFrame(hwnd);
    }

    /// <summary>
    /// Hides the banner.
    /// </summary>
    public void Hide() => AppWindow.Hide();
}
