using System.ComponentModel;
using Microsoft.Win32;

namespace Fenpo.VirtualCamera;

/// <summary>
/// The DLL's COM entry points: class object for <see cref="CameraActivator"/>, and registration in HKLM (regsvr32 run
/// by Fenpo's administrator-approved install), because Frame Server does not see packaged COM classes (S7).
/// </summary>
public static partial class ComServer
{
    /// <summary>
    /// Fenpo Kamera's class id; Fenpo.exe passes it to MFCreateVirtualCamera.
    /// </summary>
    public const string ClassId = "8D2E6A54-3C1B-4F8E-A6C9-7B5E2F1D0C34";

    /// <summary>
    /// Returns the class factory.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = nameof(DllGetClassObject))]
    public static unsafe uint DllGetClassObject(nint classId, nint interfaceId, nint result) => Guard(() =>
    {
        *(nint*)result = 0;

        if (*(Guid*)classId != new Guid(ClassId) || *(Guid*)interfaceId != typeof(IClassFactory).GUID)
        {
            return Constants.CLASS_E_CLASSNOTAVAILABLE;
        }

        var factory = ComObject.GetOrCreateComInstance(new ClassFactory(), *(Guid*)interfaceId);
        *(nint*)result = factory;
        return factory == 0 ? Constants.E_NOINTERFACE : Constants.S_OK;
    });

    /// <summary>
    /// Frame Server keeps the DLL loaded.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = nameof(DllCanUnloadNow))]
    public static uint DllCanUnloadNow() => (uint)Constants.S_FALSE;

    /// <summary>
    /// Registers the class in HKLM with this DLL's path.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = nameof(DllRegisterServer))]
    public static uint DllRegisterServer() => Guard(() =>
    {
        using var key = Registry.LocalMachine.CreateSubKey($@"Software\Classes\CLSID\{{{ClassId}}}");
        key.SetValue(null, "Fenpo Kamera");
        using var server = key.CreateSubKey("InprocServer32");
        server.SetValue(null, DllPath());
        server.SetValue("ThreadingModel", "Both");
        return Constants.S_OK;
    });

    /// <summary>
    /// Removes the registration.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = nameof(DllUnregisterServer))]
    public static uint DllUnregisterServer() => Guard(() =>
    {
        Registry.LocalMachine.DeleteSubKeyTree($@"Software\Classes\CLSID\{{{ClassId}}}", throwOnMissingSubKey: false);
        return Constants.S_OK;
    });

    /// <summary>
    /// Writes a line to %ProgramData%\Fenpo\Camera\camera.log when the folder exists (the install creates it and lets
    /// Frame Server's account write there). Errors only; the camera must not slow down for logging.
    /// </summary>
    /// <param name="text">What happened.</param>
    public static void Log(string text)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Fenpo", "Camera");

            if (Directory.Exists(folder))
            {
                File.AppendAllText(Path.Combine(folder, "camera.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {text}{Environment.NewLine}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static uint Guard(Func<HRESULT> action)
    {
        try
        {
            return (uint)action();
        }
        catch (Exception exception)
        {
            Log($"COM entry failed: {exception}");
            return (uint)exception.HResult;
        }
    }

    private static unsafe string DllPath()
    {
        const int fromAddress = 4;
        const int unchangedRefCount = 2;
        delegate* unmanaged<uint> any = &DllCanUnloadNow;

        if (!GetModuleHandleExW(fromAddress | unchangedRefCount, (nint)any, out var module))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var name = stackalloc char[1024];
        var length = GetModuleFileNameW(module, name, 1024);
        return length == 0 ? throw new Win32Exception(Marshal.GetLastPInvokeError()) : new string(name, 0, (int)length);
    }

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, nint address, out nint module);

    [LibraryImport("kernel32", SetLastError = true)]
    private static unsafe partial uint GetModuleFileNameW(nint module, char* name, uint size);
}

/// <summary>
/// Creates <see cref="CameraActivator"/> objects.
/// </summary>
[GeneratedComClass]
public partial class ClassFactory : IClassFactory
{
    HRESULT IClassFactory.CreateInstance(nint outer, in Guid iid, out nint instance)
    {
        if (outer != 0)
        {
            instance = 0;
            return Constants.CLASS_E_NOAGGREGATION;
        }

        instance = ComObject.GetOrCreateComInstance(new CameraActivator(), iid);
        return instance == 0 ? Constants.E_NOINTERFACE : Constants.S_OK;
    }

    HRESULT IClassFactory.LockServer(BOOL doLock) => Constants.S_OK;
}
