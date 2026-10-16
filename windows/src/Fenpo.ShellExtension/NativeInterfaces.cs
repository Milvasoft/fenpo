using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Fenpo.ShellExtension;

/// <summary>
/// IClassFactory: Explorer asks it for the command object.
/// </summary>
[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, in Guid iid, out nint instance);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool doLock);
}

/// <summary>
/// IExplorerCommand: one entry in File Explorer's right-click menu.
/// </summary>
[GeneratedComInterface]
[Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
internal partial interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(nint items, out nint name);

    [PreserveSig]
    int GetIcon(nint items, out nint icon);

    [PreserveSig]
    int GetToolTip(nint items, out nint tip);

    [PreserveSig]
    int GetCanonicalName(out Guid name);

    [PreserveSig]
    int GetState(nint items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);

    [PreserveSig]
    int Invoke(nint items, nint bindContext);

    [PreserveSig]
    int GetFlags(out uint flags);

    [PreserveSig]
    int EnumSubCommands(out nint commands);
}

/// <summary>
/// IShellItemArray: the selected items.
/// </summary>
[GeneratedComInterface]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
internal partial interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(nint bindContext, in Guid handler, in Guid iid, out nint result);

    [PreserveSig]
    int GetPropertyStore(int flags, in Guid iid, out nint result);

    [PreserveSig]
    int GetPropertyDescriptionList(nint keyType, in Guid iid, out nint result);

    [PreserveSig]
    int GetAttributes(int flags, uint mask, out uint attributes);

    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetItemAt(uint index, out nint item);

    [PreserveSig]
    int EnumItems(out nint items);
}

/// <summary>
/// IShellItem: one selected item.
/// </summary>
[GeneratedComInterface]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem
{
    [PreserveSig]
    int BindToHandler(nint bindContext, in Guid handler, in Guid iid, out nint result);

    [PreserveSig]
    int GetParent(out nint parent);

    [PreserveSig]
    int GetDisplayName(uint kind, out nint name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare(nint other, uint hint, out int order);
}

/// <summary>
/// Win32 functions the command needs.
/// </summary>
internal static unsafe partial class NativeMethods
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);
    public const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

    // SIGDN_FILESYSPATH: a file system path, or failure for virtual items.
    public const uint SigdnFileSysPath = 0x80058000;

    // SFGAO_FILESYSTEM | SFGAO_FOLDER.
    public const uint SfgaoFileSystem = 0x40000000;
    public const uint SfgaoFolder = 0x20000000;

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial int SHLoadIndirectString(string source, char* output, uint size, nint reserved);

    [LibraryImport("kernel32.dll")]
    public static unsafe partial int GetCurrentPackageFullName(ref uint length, char* name);

    [LibraryImport("kernel32.dll")]
    public static unsafe partial int GetCurrentPackagePath(ref uint length, char* path);
}
