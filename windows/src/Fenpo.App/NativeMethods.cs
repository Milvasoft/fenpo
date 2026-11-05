using System.Runtime.InteropServices;
using Fenpo.Core.Calls;

namespace Fenpo.App;

/// <summary>
/// Win32 calls the Windows App SDK does not wrap.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// MB_ICONINFORMATION.
    /// </summary>
    internal const uint MessageBoxIconInformation = 0x40;

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    /// <summary>
    /// Whether this process runs with package identity (started from the package, not from Fenpo.exe directly).
    /// </summary>
    /// <returns>False when GetCurrentPackageFullName reports APPMODEL_ERROR_NO_PACKAGE.</returns>
    internal static bool HasPackageIdentity()
    {
        var length = 0u;
        return GetCurrentPackageFullName(ref length, 0) != AppModelErrorNoPackage;
    }

    private const int AppModelErrorNoPackage = 15700;

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(nint owner, string text, string caption, uint type);

    /// <summary>
    /// Splits a command line the way Windows does for a program's arguments.
    /// </summary>
    /// <param name="commandLine">Command line, with or without the program.</param>
    /// <returns>The parts.</returns>
    internal static unsafe IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var parts = CommandLineToArgvW(commandLine, out var count);

        if (parts == 0)
        {
            return [];
        }

        try
        {
            var result = new List<string>(count);

            for (var index = 0; index < count; index++)
            {
                result.Add(Marshal.PtrToStringUni(((nint*)parts)[index]) ?? string.Empty);
            }

            return result;
        }
        finally
        {
            LocalFree(parts);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CommandLineToArgvW(string commandLine, out int count);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

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
    /// Whether a game, a presentation or another full-screen app is in front. Windows reports the ones it recognizes;
    /// a borderless game does not always count for it, so the front window's size is checked as well.
    /// </summary>
    /// <returns>True when a window on top would get in the way.</returns>
    internal static unsafe bool IsFullScreenAppInFront()
    {
        const int busy = 2; // QUNS_BUSY
        const int direct3D = 3; // QUNS_RUNNING_D3D_FULL_SCREEN
        const int presentation = 4; // QUNS_PRESENTATION_MODE
        const int style = -16; // GWL_STYLE
        const nint caption = 0xC00000; // WS_CAPTION
        const uint nearest = 2; // MONITOR_DEFAULTTONEAREST

        if (SHQueryUserNotificationState(out var state) == 0 && state is busy or direct3D or presentation)
        {
            return true;
        }

        var window = GetForegroundWindow();

        if (window == 0 || IsDesktop(window) || !GetWindowRect(window, out var rect))
        {
            return false;
        }

        var monitor = new MonitorInfo { Size = sizeof(MonitorInfo) };

        if (!GetMonitorInfo(MonitorFromWindow(window, nearest), ref monitor))
        {
            return false;
        }

        var maximizedWithTitleBar = IsZoomed(window) && (GetWindowLongPtr(window, style) & caption) == caption;
        return FullScreenRule.FillsMonitor(rect.ToScreenRect(), monitor.Monitor.ToScreenRect(), maximizedWithTitleBar);
    }

    // The desktop's own windows fill the screen when nothing else is in front.
    private static unsafe bool IsDesktop(nint window)
    {
        var name = stackalloc char[32];
        var length = GetClassName(window, name, 32);
        var className = new string(name, 0, length);
        return className is "Progman" or "WorkerW";
    }

    private static nint _systemQueueController;

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint hwnd, char* name, int size);

    /// <summary>
    /// Creates a Windows.System dispatcher queue on this thread if it has none (the system compositor needs one).
    /// </summary>
    internal static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null || _systemQueueController != 0)
        {
            return;
        }

        // DQTYPE_THREAD_CURRENT, DQTAT_COM_NONE. The controller is kept for the life of the process.
        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 0 };
        _ = CreateDispatcherQueueController(options, out _systemQueueController);
    }

    /// <summary>
    /// Turns off the DWM border and rounded corners, for windows that set their own shape.
    /// </summary>
    /// <param name="hwnd">Window handle.</param>
    internal static void RemoveFrame(nint hwnd)
    {
        const int cornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
        const int borderColor = 34; // DWMWA_BORDER_COLOR
        var doNotRound = 1; // DWMWCP_DONOTROUND
        var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        _ = DwmSetWindowAttribute(hwnd, cornerPreference, ref doNotRound, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, borderColor, ref none, sizeof(int));

        // The frame across the whole client area lets transparent composition show the desktop instead of black.
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly ScreenRect ToScreenRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }
}
