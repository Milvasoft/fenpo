using Fenpo.App.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Fenpo.App;

/// <summary>
/// The island: a dark pill at the top center of the screen for an incoming call. Shown without taking the focus.
/// </summary>
public sealed partial class IslandWindow : Window
{
    private const int _width = 420;
    private const int _height = 76;
    private const int _top = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="IslandWindow"/> class.
    /// </summary>
    public IslandWindow()
    {
        InitializeComponent();

        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        NativeMethods.HideFromSwitchers(WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Raised when the user answers (true) or declines (false) the shown call.
    /// </summary>
    public event EventHandler<bool>? Responded;

    /// <summary>Gets the caller name.</summary>
    public string CallerName { get; private set; } = string.Empty;

    /// <summary>Gets the line under the name.</summary>
    public string CallerDetail { get; private set; } = string.Empty;

    /// <summary>Gets the avatar letter.</summary>
    public string CallerInitial { get; private set; } = string.Empty;

    /// <summary>Gets the avatar fill.</summary>
    public Brush? CallerFill { get; private set; }

    /// <summary>Gets a value indicating whether the phone offers an answer action.</summary>
    public bool CanAnswer { get; private set; }

    /// <summary>
    /// Shows a call.
    /// </summary>
    /// <param name="name">Caller.</param>
    /// <param name="detail">Line under the name.</param>
    /// <param name="canAnswer">Whether the phone offers an answer action.</param>
    public void ShowCall(string name, string detail, bool canAnswer)
    {
        CallerName = name;
        CallerDetail = detail;
        CallerInitial = Avatar.Initial(name);
        CallerFill = Avatar.Fill(name);
        CanAnswer = canAnswer;

        // x:Bind on a Window does not refresh from a PropertyChanged with an empty name after the first render.
        Bindings.Update();

        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96d;
        var size = new SizeInt32((int)(_width * scale), (int)(_height * scale));
        var area = DisplayArea.Primary.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(area.X + ((area.Width - size.Width) / 2), area.Y + (int)(_top * scale), size.Width, size.Height));
        AppWindow.Show(activateWindow: false);

        // No DWM border or corners: the transparent window shows only the XAML pill.
        NativeMethods.RemoveFrame(hwnd);
        AppearStoryboard.Begin();
        RingStoryboard.Begin();
    }

    /// <summary>
    /// Hides the island.
    /// </summary>
    public void Hide()
    {
        RingStoryboard.Stop();
        AppWindow.Hide();
    }

    private void OnDeclineClick(object sender, RoutedEventArgs e) => Respond(false);

    private void OnAnswerClick(object sender, RoutedEventArgs e) => Respond(true);

    private void Respond(bool answer)
    {
        Hide();
        Responded?.Invoke(this, answer);
    }
}
