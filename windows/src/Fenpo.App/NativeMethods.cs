using System.Runtime.InteropServices;

namespace Fenpo.App;

/// <summary>
/// Win32 calls the Windows App SDK does not wrap.
/// </summary>
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LockWorkStation();

    /// <summary>
    /// Asks DWM for the standard Windows 11 rounded corners (popup windows get square ones by default).
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    internal static void UseRoundCorners(nint hwnd)
    {
        const int cornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
        var round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(hwnd, cornerPreference, ref round, sizeof(int));
    }

    /// <summary>
    /// Keeps a window out of the taskbar and Alt+Tab (AppWindow.IsShownInSwitchers throws E_NOTIMPL here).
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    internal static void HideFromSwitchers(nint hwnd)
    {
        const int exStyle = -20; // GWL_EXSTYLE
        const nint toolWindow = 0x80; // WS_EX_TOOLWINDOW
        const nint appWindow = 0x40000; // WS_EX_APPWINDOW
        _ = SetWindowLongPtr(hwnd, exStyle, (GetWindowLongPtr(hwnd, exStyle) | toolWindow) & ~appWindow);
    }

    /// <summary>
    /// Clips a window to a pill (rounded ends), for the island. WinUI windows cannot be transparent.
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    internal static void ClipToPill(nint hwnd, int width, int height)
    {
        // The system owns the region after SetWindowRgn succeeds.
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, height, height);
        _ = SetWindowRgn(hwnd, region, true);
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
