using System.Diagnostics;
using System.Runtime.InteropServices;
using Fenpo.App.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Playback;

namespace Fenpo.App.Controls;

/// <summary>
/// Shows a <see cref="LiveH264Player"/>'s picture. The player decodes; every frame it has ready is copied into this
/// control's own swap chain and shown at the next screen refresh. MediaPlayerElement drew the same frames unevenly
/// (the picture froze and jumped while the phone's frames came on time), while Fenpo Kamera, which hands frames out
/// itself, stayed smooth. The picture keeps its aspect ratio.
/// </summary>
public sealed unsafe partial class LiveVideoView : UserControl
{
    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidDxgiFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    private static readonly Guid IidDxgiSurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
    private static readonly Guid IidSwapChainPanelNative = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    private readonly ILogger<LiveVideoView> _logger = App.GetService<ILogger<LiveVideoView>>();
    private readonly Lock _gate = new();
    private readonly Stopwatch _window = Stopwatch.StartNew();
    private readonly Stopwatch _sinceFrame = new();
    private MediaPlayer? _player;
    private nint _device;
    private nint _swapChain;
    private IDirect3DSurface? _surface;
    private int _frames;
    private TimeSpan _longestGap;
    private int _failures;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveVideoView"/> class.
    /// </summary>
    public LiveVideoView()
    {
        InitializeComponent();
        Unloaded += (sender, e) => Show(null);
    }

    /// <summary>
    /// Shows a player's picture, or nothing. Called on the UI thread.
    /// </summary>
    /// <param name="player">The player, or null to clear.</param>
    public void Show(LiveH264Player? player)
    {
        Release();

        if (player is null)
        {
            return;
        }

        var (width, height) = player.Size;

        try
        {
            CreateSwapChain(width, height);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "The live picture's swap chain could not be made");
            Release();
            return;
        }

        Panel.Width = width;
        Panel.Height = height;

        lock (_gate)
        {
            _player = player.Player;
        }

        player.Player.VideoFrameAvailable += OnVideoFrameAvailable;
    }

    // On the player's thread, for every frame that is due.
    private void OnVideoFrameAvailable(MediaPlayer sender, object args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _player) || _surface is null)
            {
                return;
            }

            try
            {
                sender.CopyFrameToVideoSurface(_surface);
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(_swapChain, 8))(_swapChain, 1, 0));
                Measure();
            }
            catch (Exception exception) when (exception is COMException or ObjectDisposedException or InvalidOperationException)
            {
                if (_failures++ % 100 == 0)
                {
                    _logger.LogWarning("Showing a live frame failed ({Count} so far): {Reason}", _failures, exception.Message);
                }
            }
        }
    }

    // Every few seconds: how many frames went to the screen and the longest wait between two.
    private void Measure()
    {
        var gap = _sinceFrame.IsRunning ? _sinceFrame.Elapsed : TimeSpan.Zero;
        _sinceFrame.Restart();
        _longestGap = gap > _longestGap ? gap : _longestGap;
        _frames++;

        if (_window.Elapsed >= TimeSpan.FromSeconds(3))
        {
            _logger.LogInformation("Live picture: {Fps:F1} frames/s shown, longest gap {Gap} ms", _frames / _window.Elapsed.TotalSeconds, (int)_longestGap.TotalMilliseconds);
            _frames = 0;
            _longestGap = TimeSpan.Zero;
            _window.Restart();
        }
    }

    private void CreateSwapChain(uint width, uint height)
    {
        // BGRA for the swap chain, video support for the player's copies.
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20 | 0x800, 0, 0, 7, out _device, out _, out var context));
        Marshal.Release(context);

        nint dxgiDevice = 0, adapter = 0, factory = 0, buffer = 0, inspectable = 0, panel = 0;

        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_device, in IidDxgiDevice, out dxgiDevice));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(dxgiDevice, 7))(dxgiDevice, &adapter));
            var iidFactory = IidDxgiFactory2;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(adapter, 6))(adapter, &iidFactory, &factory));

            var description = new SwapChainDescription
            {
                Width = width,
                Height = height,
                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                SampleCount = 1,
                BufferUsage = 0x20, // DXGI_USAGE_RENDER_TARGET_OUTPUT
                BufferCount = 2,
                Scaling = 0, // DXGI_SCALING_STRETCH
                SwapEffect = 3, // DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL
                AlphaMode = 3, // DXGI_ALPHA_MODE_IGNORE: every frame covers the whole buffer
            };
            nint swapChain;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, SwapChainDescription*, nint, nint*, int>)Slot(factory, 24))(factory, _device, &description, 0, &swapChain));
            _swapChain = swapChain;

            // With the flip model, buffer 0 is always the one to draw into next.
            var iidSurface = IidDxgiSurface;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Slot(_swapChain, 9))(_swapChain, 0, &iidSurface, &buffer));
            Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(buffer, out inspectable));
            var surface = WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(inspectable);

            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(((WinRT.IWinRTObject)Panel).NativeObject.ThisPtr, in IidSwapChainPanelNative, out panel));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(panel, 3))(panel, _swapChain));

            lock (_gate)
            {
                _surface = surface;
            }
        }
        finally
        {
            foreach (var pointer in (ReadOnlySpan<nint>)[dxgiDevice, adapter, factory, buffer, inspectable, panel])
            {
                if (pointer != 0)
                {
                    Marshal.Release(pointer);
                }
            }
        }
    }

    private void Release()
    {
        MediaPlayer? player;
        IDirect3DSurface? surface;
        nint swapChain, device;

        lock (_gate)
        {
            (player, surface, swapChain, device) = (_player, _surface, _swapChain, _device);
            (_player, _surface, _swapChain, _device) = (null, null, 0, 0);
            _sinceFrame.Reset();
        }

        // The page may have disposed the player already.
        try
        {
            if (player is not null)
            {
                player.VideoFrameAvailable -= OnVideoFrameAvailable;
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or COMException)
        {
        }

        if (swapChain != 0 && Marshal.QueryInterface(((WinRT.IWinRTObject)Panel).NativeObject.ThisPtr, in IidSwapChainPanelNative, out var panel) == 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(panel, 3))(panel, 0);
            Marshal.Release(panel);
        }

        surface?.Dispose();

        if (swapChain != 0)
        {
            Marshal.Release(swapChain);
        }

        if (device != 0)
        {
            Marshal.Release(device);
        }
    }

    // A COM method's address: slot n of the instance's vtable.
    private static nint Slot(nint instance, int slot) => (*(nint**)instance)[slot];

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11SurfaceFromDXGISurface(nint dxgiSurface, out nint graphicsSurface);

    [StructLayout(LayoutKind.Sequential)]
    private struct SwapChainDescription
    {
        public uint Width;
        public uint Height;
        public int Format;
        public int Stereo;
        public uint SampleCount;
        public uint SampleQuality;
        public uint BufferUsage;
        public uint BufferCount;
        public int Scaling;
        public int SwapEffect;
        public int AlphaMode;
        public uint Flags;
    }
}
