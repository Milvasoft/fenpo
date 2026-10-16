using System.Runtime.InteropServices;

namespace Fenpo.App.Helpers;

/// <summary>
/// The master volume of the default playback device (Core Audio, IAudioEndpointVolume), for the phone's volume
/// buttons on "Playing on the PC".
/// </summary>
internal static class SystemVolume
{
    private static readonly Guid _audioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private enum DataFlow
    {
        Render = 0,
    }

    private enum Role
    {
        Multimedia = 1,
    }

    /// <summary>
    /// Gets the volume from 0 to 100, or null when there is no playback device.
    /// </summary>
    /// <returns>Percent.</returns>
    public static uint? Get()
    {
        try
        {
            var endpoint = Endpoint();
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var level));
            return (uint)Math.Round(level * 100);
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sets the volume.
    /// </summary>
    /// <param name="percent">0 to 100.</param>
    /// <returns>True when set.</returns>
    public static bool Set(uint percent)
    {
        try
        {
            var endpoint = Endpoint();
            var context = Guid.Empty;
            return endpoint.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref context) >= 0;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            return false;
        }
    }

    private static IAudioEndpointVolume Endpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device));
        var iid = _audioEndpointVolume;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 1, IntPtr.Zero, out var activated));
        return (IAudioEndpointVolume)activated;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);

        int UnregisterControlChangeNotify(IntPtr notify);

        int GetChannelCount(out uint count);

        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        int GetMasterVolumeLevel(out float levelDb);

        int GetMasterVolumeLevelScalar(out float level);
    }
}
