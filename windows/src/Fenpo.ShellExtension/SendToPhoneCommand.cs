using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Fenpo.ShellExtension;

/// <summary>
/// "Send to phone with Fenpo" for the selected files: starts Fenpo with "--send" and the paths; a running Fenpo takes
/// them through its single-instance redirection and sends them to the phone in use.
/// </summary>
[GeneratedComClass]
[Guid(ClassId)]
internal sealed partial class SendToPhoneCommand : IExplorerCommand
{
    /// <summary>
    /// The class id in Package.appxmanifest (com:Class and desktop5:Verb).
    /// </summary>
    public const string ClassId = "6E1C4C2A-6F2B-4E43-9B8C-0C3F1E9E5A11";

    /// <inheritdoc/>
    public int GetTitle(nint items, out nint name)
    {
        // The menu text lives in Fenpo's resources (both languages); Windows picks the user's language.
        name = Marshal.StringToCoTaskMemUni(Resource("Shell_SendToPhone") ?? "Fenpo");
        return NativeMethods.S_OK;
    }

    /// <inheritdoc/>
    public int GetIcon(nint items, out nint icon)
    {
        icon = PackagePath() is { } path ? Marshal.StringToCoTaskMemUni(Path.Combine(path, "Assets", "Fenpo.ico")) : 0;
        return icon == 0 ? NativeMethods.E_NOTIMPL : NativeMethods.S_OK;
    }

    /// <inheritdoc/>
    public int GetToolTip(nint items, out nint tip)
    {
        tip = 0;
        return NativeMethods.E_NOTIMPL;
    }

    /// <inheritdoc/>
    public int GetCanonicalName(out Guid name)
    {
        name = new Guid(ClassId);
        return NativeMethods.S_OK;
    }

    /// <inheritdoc/>
    public int GetState(nint items, bool okToBeSlow, out uint state)
    {
        // Enabled; folders and virtual items are left out when sending.
        state = 0;
        return NativeMethods.S_OK;
    }

    /// <inheritdoc/>
    public int Invoke(nint items, nint bindContext)
    {
        try
        {
            var paths = FilePaths(items);

            if (paths.Count == 0)
            {
                return NativeMethods.S_OK;
            }

            var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "fenpo.exe");
            var start = new ProcessStartInfo(alias) { UseShellExecute = false };
            start.ArgumentList.Add("--send");

            foreach (var path in paths)
            {
                start.ArgumentList.Add(path);
            }

            Process.Start(start)?.Dispose();
            return NativeMethods.S_OK;
        }
        catch (Exception exception)
        {
            return exception.HResult == 0 ? NativeMethods.E_FAIL : exception.HResult;
        }
    }

    /// <inheritdoc/>
    public int GetFlags(out uint flags)
    {
        flags = 0;
        return NativeMethods.S_OK;
    }

    /// <inheritdoc/>
    public int EnumSubCommands(out nint commands)
    {
        commands = 0;
        return NativeMethods.E_NOTIMPL;
    }

    private static unsafe List<string> FilePaths(nint items)
    {
        var paths = new List<string>();

        if (items == 0)
        {
            return paths;
        }

        var array = ComInterfaceMarshaller<IShellItemArray>.ConvertToManaged((void*)items);

        if (array is null || array.GetCount(out var count) != NativeMethods.S_OK)
        {
            return paths;
        }

        for (uint index = 0; index < count; index++)
        {
            if (array.GetItemAt(index, out var pointer) != NativeMethods.S_OK || pointer == 0)
            {
                continue;
            }

            try
            {
                var item = ComInterfaceMarshaller<IShellItem>.ConvertToManaged((void*)pointer);

                // Files on disk only: folders and items without a path (phones, libraries) are skipped.
                if (item is null
                    || item.GetAttributes(NativeMethods.SfgaoFileSystem | NativeMethods.SfgaoFolder, out var attributes) < 0
                    || (attributes & NativeMethods.SfgaoFolder) != 0
                    || (attributes & NativeMethods.SfgaoFileSystem) == 0
                    || item.GetDisplayName(NativeMethods.SigdnFileSysPath, out var name) != NativeMethods.S_OK)
                {
                    continue;
                }

                paths.Add(Marshal.PtrToStringUni(name) ?? string.Empty);
                Marshal.FreeCoTaskMem(name);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }

        paths.RemoveAll(string.IsNullOrEmpty);
        return paths;
    }

    private static unsafe string? Resource(string name)
    {
        if (PackageFullName() is not { } package)
        {
            return null;
        }

        var output = stackalloc char[512];
        var result = NativeMethods.SHLoadIndirectString($"@{{{package}? ms-resource:///Resources/{name}}}", output, 512, 0);
        return result == NativeMethods.S_OK ? new string(output) : null;
    }

    private static unsafe string? PackageFullName()
    {
        uint length = 0;
        NativeMethods.GetCurrentPackageFullName(ref length, null);

        if (length is 0 or > 1024)
        {
            return null;
        }

        var name = stackalloc char[(int)length];
        return NativeMethods.GetCurrentPackageFullName(ref length, name) == 0 ? new string(name) : null;
    }

    private static unsafe string? PackagePath()
    {
        uint length = 0;
        NativeMethods.GetCurrentPackagePath(ref length, null);

        if (length is 0 or > 1024)
        {
            return null;
        }

        var path = stackalloc char[(int)length];
        return NativeMethods.GetCurrentPackagePath(ref length, path) == 0 ? new string(path) : null;
    }
}
