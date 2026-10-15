using DirectN;
using DirectN.Extensions.Com;
using DirectN.Extensions.Utilities;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

// Usage: VCamHost [--all-users] [--seconds N] [--snapshot <png path>]
// Creates the "Fenpo S7" virtual camera backed by the VCamNetSample AOT media source (CLSID below), starts it,
// optionally grabs one frame through MediaCapture like any camera app would, and removes it after N seconds.
const string sourceClsid = "{570b1532-4fcb-472f-ae37-b5daf86abfd4}";
const string friendlyName = "Fenpo S7";

var allUsers = args.Contains("--all-users");
var secondsIndex = Array.IndexOf(args, "--seconds");
var seconds = secondsIndex >= 0 ? int.Parse(args[secondsIndex + 1]) : 20;
var snapshotIndex = Array.IndexOf(args, "--snapshot");
var snapshot = snapshotIndex >= 0 ? args[snapshotIndex + 1] : null;

Log($"Base directory {AppContext.BaseDirectory}");
TryLog(() => $"Package {Windows.ApplicationModel.Package.Current.Id.FullName}");

Functions.MFStartup(Constants.MF_VERSION, 0).ThrowOnError();

using var name = new Pwstr(friendlyName);
using var source = new Pwstr(sourceClsid);
var hr = Functions.MFCreateVirtualCamera(
    MFVirtualCameraType.MFVirtualCameraType_SoftwareCameraSource,
    MFVirtualCameraLifetime.MFVirtualCameraLifetime_Session,
    allUsers ? MFVirtualCameraAccess.MFVirtualCameraAccess_AllUsers : MFVirtualCameraAccess.MFVirtualCameraAccess_CurrentUser,
    name,
    source,
    0,
    0,
    out var created);
Log($"MFCreateVirtualCamera ({(allUsers ? "AllUsers" : "CurrentUser")}): {Describe(hr)}");

if (hr.IsError)
{
    return 1;
}

using var camera = new ComObject<IMFVirtualCamera>(created);
hr = camera.Object.Start(null);
Log($"IMFVirtualCamera.Start: {Describe(hr)}");

if (hr.IsSuccess && snapshot is not null)
{
    await Task.Delay(2000);
    await SnapshotAsync(snapshot);
}

if (hr.IsSuccess)
{
    Log($"Camera running for {seconds} s (open the Camera app to look at it)");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
}

Log($"IMFVirtualCamera.Remove: {Describe(camera.Object.Remove())}");
Functions.MFShutdown();
return hr.IsSuccess ? 0 : 1;

static async Task SnapshotAsync(string path)
{
    var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
    Log($"Video capture devices: {string.Join(", ", cameras.Select(c => $"\"{c.Name}\""))}");
    var ours = cameras.FirstOrDefault(c => c.Name == friendlyName);

    if (ours is null)
    {
        Log("The virtual camera is not in the device list");
        return;
    }

    try
    {
        using var capture = new MediaCapture();
        await capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            VideoDeviceId = ours.Id,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        });

        using var stream = new InMemoryRandomAccessStream();
        await capture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreatePng(), stream);
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
        Log($"Snapshot {bytes.Length} bytes -> {path}");
    }
    catch (Exception exception)
    {
        Log($"Snapshot failed: {exception.GetType().Name} 0x{exception.HResult:X8} {exception.Message}");
    }
}

static string Describe(HRESULT hr) => hr.IsSuccess ? "S_OK" : $"0x{(uint)hr.Value:X8} {new System.ComponentModel.Win32Exception(hr.Value).Message}";

static void Log(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");

static void TryLog(Func<string> message)
{
    try
    {
        Log(message());
    }
    catch (Exception exception)
    {
        Log($"{exception.GetType().Name} 0x{exception.HResult:X8}");
    }
}
