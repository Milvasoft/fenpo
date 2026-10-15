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
}
