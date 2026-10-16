using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Fenpo.App;

/// <summary>
/// The phone's laser pointer: a small transparent always-on-top window with a dot, moved over the primary screen
/// without taking the focus.
/// </summary>
public sealed partial class LaserWindow : Window
{
    private const int _size = 40;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaserWindow"/> class.
    /// </summary>
    public LaserWindow()
    {
        InitializeComponent();
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        NativeMethods.HideFromSwitchers(WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Shows the dot at a point of the primary screen.
    /// </summary>
    /// <param name="x">0..1 from the left.</param>
    /// <param name="y">0..1 from the top.</param>
    public void ShowAt(float x, float y)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var size = (int)(_size * NativeMethods.GetDpiForWindow(hwnd) / 96d);
        var screen = DisplayArea.Primary.OuterBounds;
        var left = screen.X + (int)(Math.Clamp(x, 0, 1) * screen.Width) - (size / 2);
        var top = screen.Y + (int)(Math.Clamp(y, 0, 1) * screen.Height) - (size / 2);
        AppWindow.MoveAndResize(new RectInt32(left, top, size, size));

        if (!AppWindow.IsVisible)
        {
            AppWindow.Show(activateWindow: false);
            NativeMethods.RemoveFrame(hwnd);
        }
    }

    /// <summary>
    /// Hides the dot.
    /// </summary>
    public void HideDot() => AppWindow.Hide();
}
