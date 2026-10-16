using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Fenpo.Core.Camera;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Fenpo.App.Services;

/// <summary>
/// Fenpo Kamera on the PC: installs the media source (Fenpo.VirtualCamera.dll, HKLM, with the user's administrator
/// approval), makes Windows show "Fenpo Kamera" while Fenpo runs (MFCreateVirtualCamera), starts the phone's camera
/// when an app opens it and stops it when the app is done, and writes the phone's frames for the media source
/// (<see cref="CameraRing"/>).
/// </summary>
public sealed partial class VirtualCameraService(CameraService camera, ILogger<VirtualCameraService> logger) : IHostedService
{
    private const string _classId = "{8D2E6A54-3C1B-4F8E-A6C9-7B5E2F1D0C34}";
    private const string _dllName = "Fenpo.VirtualCamera.dll";
    private static readonly TimeSpan _readerStale = TimeSpan.FromSeconds(3);

    private readonly CameraService _camera = camera;
    private readonly ILogger<VirtualCameraService> _logger = logger;
    private readonly Lock _writeLock = new();
    private readonly CancellationTokenSource _stopping = new();
    private nint _virtualCamera;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private unsafe byte* _memory;
    private byte[] _config = [];
    private bool _startedForApp;

    /// <summary>
    /// Raised from any thread when installed or in use changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value indicating whether the media source is installed (registered in HKLM, file present).
    /// </summary>
    public bool IsInstalled => InstalledPath() is { } path && File.Exists(path);

    /// <summary>
    /// Gets a value indicating whether the installed media source is older than the one in this Fenpo.
    /// </summary>
    public bool NeedsUpdate => InstalledPath() is { } path && File.Exists(path) && File.Exists(PackagedPath) && new System.IO.FileInfo(path).Length != new System.IO.FileInfo(PackagedPath).Length;

    /// <summary>
    /// Gets a value indicating whether an app has Fenpo Kamera open.
    /// </summary>
    public bool InUse { get; private set; }

    private static string PackagedPath => Path.Combine(AppContext.BaseDirectory, _dllName);

    private static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Fenpo", "Camera");

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        RemoveCamera();
        _view?.Dispose();
        _mapping?.Dispose();
    }

    /// <summary>
    /// Installs or updates the media source; Windows asks the user for administrator approval.
    /// </summary>
    /// <returns>True when installed.</returns>
    public async Task<bool> InstallAsync()
    {
        var target = Path.Combine(InstallFolder, _dllName);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Fenpo", "Camera");

        // Frame Server holds the old DLL while it runs; it starts again when an app opens a camera.
        // The log folder lets Frame Server's account (Local Service) write the camera's error log.
        var command = string.Join(" & ",
            "sc stop FrameServer >nul 2>&1",
            "sc stop FrameServerMonitor >nul 2>&1",
            $"mkdir \"{InstallFolder}\" 2>nul",
            $"copy /y \"{PackagedPath}\" \"{target}\" >nul",
            $"mkdir \"{data}\" 2>nul",
            $"icacls \"{data}\" /grant *S-1-5-19:(OI)(CI)M >nul",
            $"regsvr32 /s \"{target}\"");

        if (!await RunElevatedAsync(command))
        {
            return false;
        }

        _logger.LogInformation("Fenpo Kamera installed: {Installed}", IsInstalled);
        Changed?.Invoke(this, EventArgs.Empty);

        if (IsInstalled)
        {
            await Task.Run(CreateCamera);
        }

        return IsInstalled;
    }

    /// <summary>
    /// Removes the media source; Windows asks the user for administrator approval.
    /// </summary>
    /// <returns>True when removed.</returns>
    public async Task<bool> UninstallAsync()
    {
        var target = Path.Combine(InstallFolder, _dllName);
        RemoveCamera();
        var command = string.Join(" & ",
            $"regsvr32 /u /s \"{target}\"",
            "sc stop FrameServer >nul 2>&1",
            "sc stop FrameServerMonitor >nul 2>&1",
            $"del /f /q \"{target}\" 2>nul",
            $"rmdir \"{InstallFolder}\" 2>nul");

        var done = await RunElevatedAsync(command);
        _logger.LogInformation("Fenpo Kamera removed: {Removed}", !IsInstalled);
        Changed?.Invoke(this, EventArgs.Empty);
        return done && !IsInstalled;
    }

    /// <summary>
    /// Takes a packet of the phone's camera stream for the media source.
    /// </summary>
    /// <param name="packet">A video packet.</param>
    public unsafe void Write(StreamPacket packet)
    {
        if (packet.Track != StreamTrack.Video)
        {
            return;
        }

        lock (_writeLock)
        {
            if (packet.CodecConfig)
            {
                _config = packet.Data.ToByteArray();
                return;
            }

            if (_memory == null)
            {
                return;
            }

            // The media source's decoder starts at a key frame with its parameter sets in front.
            var data = packet.KeyFrame ? [.. _config, .. packet.Data.Span] : packet.Data.ToByteArray();
            CameraRing.Write(_memory, data, (long)packet.PtsUs, packet.KeyFrame);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (IsInstalled)
        {
            CreateCamera();
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Follow();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Following Fenpo Kamera failed");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    // The media source creates the shared memory when an app opens the camera; from then on it says whether it is in
    // use, and the phone's camera follows.
    private unsafe void Follow()
    {
        if (_memory == null)
        {
            if (!TryOpenRing())
            {
                return;
            }
        }

        var inUse = CameraRing.ReaderActive(_memory, _readerStale);

        if (inUse == InUse)
        {
            return;
        }

        InUse = inUse;
        _logger.LogInformation("Fenpo Kamera {State}", inUse ? "opened by an app" : "closed");
        Changed?.Invoke(this, EventArgs.Empty);

        if (inUse && _camera.State is not (CameraState.Streaming or CameraState.Starting or CameraState.WaitingForTap))
        {
            _startedForApp = true;
            _ = _camera.StartCameraAsync("Fenpo Kamera");
        }
        else if (!inUse && _startedForApp)
        {
            _startedForApp = false;
            _ = _camera.StopCameraAsync();
        }
    }

    private unsafe bool TryOpenRing()
    {
        try
        {
            _mapping = MemoryMappedFile.OpenExisting(CameraRing.Name, MemoryMappedFileRights.ReadWrite);
            _view = _mapping.CreateViewAccessor(0, CameraRing.Size, MemoryMappedFileAccess.ReadWrite);
            byte* pointer = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);

            if (!CameraRing.IsReady(pointer))
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _view.Dispose();
                _mapping.Dispose();
                _view = null;
                _mapping = null;
                return false;
            }

            lock (_writeLock)
            {
                _memory = pointer;
            }

            _logger.LogInformation("Fenpo Kamera's shared memory is open");
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Windows shows "Fenpo Kamera" while this process runs (session lifetime) and only to this user.
    private unsafe void CreateCamera()
    {
        if (_virtualCamera != 0)
        {
            return;
        }

        try
        {
            MFStartup(0x00020070, 0);
            var result = MFCreateVirtualCamera(0, 0, 0, "Fenpo Kamera", _classId, 0, 0, out var created);

            if (result < 0)
            {
                _logger.LogWarning("Creating Fenpo Kamera failed: 0x{Result:X8}", result);
                return;
            }

            var start = ((delegate* unmanaged[Stdcall]<nint, nint, int>*)*(nint*)created)[36];
            result = start(created, 0);

            if (result < 0)
            {
                _logger.LogWarning("Starting Fenpo Kamera failed: 0x{Result:X8}", result);
                Marshal.Release(created);
                return;
            }

            _virtualCamera = created;
            _logger.LogInformation("Fenpo Kamera is available to apps");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            _logger.LogWarning(exception, "Fenpo Kamera is not available on this Windows");
        }
    }

    private unsafe void RemoveCamera()
    {
        var created = Interlocked.Exchange(ref _virtualCamera, 0);

        if (created == 0)
        {
            return;
        }

        // IMFVirtualCamera::Shutdown, then the last reference.
        var shutdown = ((delegate* unmanaged[Stdcall]<nint, int>*)*(nint*)created)[43];
        shutdown(created);
        Marshal.Release(created);
    }

    private static string? InstalledPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"Software\Classes\CLSID\{_classId}\InprocServer32");
        return key?.GetValue(null) as string;
    }

    private async Task<bool> RunElevatedAsync(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });

            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // The user said no to the administrator prompt.
            _logger.LogInformation("Fenpo Kamera install was not approved");
            return false;
        }
    }

    [LibraryImport("mfplat.dll")]
    private static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfsensorgroup.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateVirtualCamera(int type, int lifetime, int access, string friendlyName, string sourceId, nint categories, uint categoryCount, out nint virtualCamera);
}
