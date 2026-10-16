using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Fenpo.ShellExtension;

/// <summary>
/// The DLL's COM entry points: Explorer's surrogate process asks for the class factory of the command.
/// </summary>
internal static class ComServer
{
    private static readonly StrategyBasedComWrappers _wrappers = new();

    /// <summary>
    /// Returns the class factory for <see cref="SendToPhoneCommand"/>.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static unsafe int DllGetClassObject(Guid* classId, Guid* interfaceId, nint* result)
    {
        *result = 0;

        if (*classId != new Guid(SendToPhoneCommand.ClassId))
        {
            return NativeMethods.CLASS_E_CLASSNOTAVAILABLE;
        }

        return QueryInterface(new ClassFactory(), *interfaceId, out *result);
    }

    /// <summary>
    /// Explorer keeps the surrogate; the DLL never asks to be unloaded.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow() => NativeMethods.S_FALSE;

    /// <summary>
    /// Wraps a managed object as COM and asks it for <paramref name="interfaceId"/>.
    /// </summary>
    public static int QueryInterface(object instance, Guid interfaceId, out nint result)
    {
        var unknown = _wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);

        try
        {
            return Marshal.QueryInterface(unknown, in interfaceId, out result);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}

/// <summary>
/// Creates <see cref="SendToPhoneCommand"/> objects.
/// </summary>
[GeneratedComClass]
internal sealed partial class ClassFactory : IClassFactory
{
    /// <inheritdoc/>
    public int CreateInstance(nint outer, in Guid iid, out nint instance)
    {
        instance = 0;
        return outer != 0 ? NativeMethods.CLASS_E_NOAGGREGATION : ComServer.QueryInterface(new SendToPhoneCommand(), iid, out instance);
    }

    /// <inheritdoc/>
    public int LockServer(bool doLock) => NativeMethods.S_OK;
}
