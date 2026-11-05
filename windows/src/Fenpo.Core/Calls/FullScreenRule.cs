namespace Fenpo.Core.Calls;

/// <summary>
/// A rectangle on the screen, in pixels.
/// </summary>
/// <param name="Left">Left edge.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Right">Right edge.</param>
/// <param name="Bottom">Bottom edge.</param>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Decides whether the window in front fills its screen the way a game does.
/// </summary>
public static class FullScreenRule
{
    /// <summary>
    /// Whether a window fills its monitor. A maximized window with a title bar also reaches past the screen's edges,
    /// by the width of its hidden frame, and fills the screen when the taskbar hides itself; it is not full screen.
    /// </summary>
    /// <param name="window">The window's rectangle.</param>
    /// <param name="monitor">Its monitor's rectangle.</param>
    /// <param name="maximizedWithTitleBar">Whether the window is maximized and has a title bar.</param>
    /// <returns>True when the window is full screen.</returns>
    public static bool FillsMonitor(ScreenRect window, ScreenRect monitor, bool maximizedWithTitleBar)
    {
        return !maximizedWithTitleBar
            && window.Left <= monitor.Left
            && window.Top <= monitor.Top
            && window.Right >= monitor.Right
            && window.Bottom >= monitor.Bottom;
    }
}
