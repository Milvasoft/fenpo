using System.Diagnostics;
using System.Runtime.InteropServices;
using Fenpo.App.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Controls;

/// <summary>
/// Shows a <see cref="LiveH264Player"/>'s pictures on this control's own swap chain: each decoded picture is copied
/// into the back buffer and presented right away, as Fenpo Kamera hands its frames to the apps that use it. The
/// picture keeps its aspect ratio.
/// </summary>
public sealed unsafe partial class LiveVideoView : UserControl
{
    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidDxgiFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    private static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IidSwapChainPanelNative = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    private readonly ILogger<LiveVideoView> _logger = App.GetService<ILogger<LiveVideoView>>();
    private readonly Lock _gate = new();
    private LiveH264Player? _player;
    private nint _device;
    private nint _context;
    private nint _swapChain;
    private nint _backBuffer;
    private uint _width;
    private uint _height;
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
    /// Shows a player's pictures, or nothing. Called on the UI thread.
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
            _player = player;
        }

        player.FrameReady += OnFrameReady;
    }

    // On the player's thread, for every decoded picture.
    private void OnFrameReady(ReadOnlySpan<byte> bgra)
    {
        lock (_gate)
        {
            if (_backBuffer == 0 || bgra.Length < _width * _height * 4)
            {
                return;
            }

            try
            {
                fixed (byte* pixels = bgra)
                {
                    // ID3D11DeviceContext::UpdateSubresource, then IDXGISwapChain::Present at the next refresh.
                    ((delegate* unmanaged[Stdcall]<nint, nint, uint, nint, byte*, uint, uint, void>)Slot(_context, 48))(_context, _backBuffer, 0, 0, pixels, _width * 4, 0);
                }

                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(_swapChain, 8))(_swapChain, 1, 0));
            }
            catch (COMException exception)
            {
                if (_failures++ % 100 == 0)
                {
                    _logger.LogWarning("Showing a live picture failed ({Count} so far): {Reason}", _failures, exception.Message);
                }
            }
        }
    }

    private void CreateSwapChain(uint width, uint height)
    {
        // BGRA for the swap chain.
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var device, out _, out var context));

        nint dxgiDevice = 0, adapter = 0, factory = 0, backBuffer = 0, panel = 0, swapChain = 0;

        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in IidDxgiDevice, out dxgiDevice));
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
                AlphaMode = 3, // DXGI_ALPHA_MODE_IGNORE: every picture covers the whole buffer
            };
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, SwapChainDescription*, nint, nint*, int>)Slot(factory, 24))(factory, device, &description, 0, &swapChain));

            // With the flip model, buffer 0 is always the one to draw into next.
            var iidTexture = IidTexture2D;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Slot(swapChain, 9))(swapChain, 0, &iidTexture, &backBuffer));

            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(((WinRT.IWinRTObject)Panel).NativeObject.ThisPtr, in IidSwapChainPanelNative, out panel));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(panel, 3))(panel, swapChain));

            lock (_gate)
            {
                (_device, _context, _swapChain, _backBuffer, _width, _height) = (device, context, swapChain, backBuffer, width, height);
            }

            (device, context, swapChain, backBuffer) = (0, 0, 0, 0);
        }
        finally
        {
            foreach (var pointer in (ReadOnlySpan<nint>)[dxgiDevice, adapter, factory, panel, backBuffer, swapChain, context, device])
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
        LiveH264Player? player;
        nint device, context, swapChain, backBuffer;

        lock (_gate)
        {
            (player, device, context, swapChain, backBuffer) = (_player, _device, _context, _swapChain, _backBuffer);
            (_player, _device, _context, _swapChain, _backBuffer) = (null, 0, 0, 0, 0);
        }

        if (player is not null)
        {
            player.FrameReady -= OnFrameReady;
        }

        if (swapChain != 0 && Marshal.QueryInterface(((WinRT.IWinRTObject)Panel).NativeObject.ThisPtr, in IidSwapChainPanelNative, out var panel) == 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(panel, 3))(panel, 0);
            Marshal.Release(panel);
        }

        foreach (var pointer in (ReadOnlySpan<nint>)[backBuffer, swapChain, context, device])
        {
            if (pointer != 0)
            {
                Marshal.Release(pointer);
            }
        }
    }

    // A COM method's address: slot n of the instance's vtable.
    private static nint Slot(nint instance, int slot) => (*(nint**)instance)[slot];

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

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
