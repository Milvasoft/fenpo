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

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
