using Fenpo.Core.Camera;

namespace Fenpo.VirtualCamera;

/// <summary>
/// The phone's picture for the virtual camera: reads H.264 frames from <see cref="CameraRing"/>, decodes them with
/// Windows' H.264 decoder to NV12 and keeps the newest frame, scaled to fit by cropping or centring. While the phone
/// sends nothing it shows a dark placeholder, and Fenpo.exe sees that an app has the camera open (it then starts the
/// phone's camera).
/// </summary>
public sealed unsafe partial class PhoneFrames(int width, int height) : IDisposable
{
    private static readonly TimeSpan _stale = TimeSpan.FromSeconds(2);

    private readonly int _width = width;
    private readonly int _height = height;
    private readonly byte[] _latest = Placeholder(width, height);
    private readonly byte[] _packet = new byte[CameraRing.MaxFrame];
    private nint _mapping;
    private byte* _memory;
    private long _last;
    private IComObject<IMFTransform>? _decoder;
    private int _decodedWidth;
    private int _decodedHeight;
    private int _decodedStride;
    private bool _hasPicture;

    /// <summary>
    /// An app opened the camera: the ring is opened (created the first time) and marked in use.
    /// </summary>
    public void Start()
    {
        Open();

        if (_memory != null)
        {
            CameraRing.SetReader(_memory, active: true);
        }
    }

    /// <summary>
    /// The app closed the camera.
    /// </summary>
    public void Stop()
    {
        if (_memory != null)
        {
            CameraRing.SetReader(_memory, active: false);
        }

        _decoder?.Dispose();
        _decoder = null;
        _last = 0;
    }

    /// <summary>
    /// Writes the newest frame into a sample buffer (NV12, stride = width).
    /// </summary>
    /// <param name="target">Sample buffer.</param>
    /// <param name="length">Its size.</param>
    public void Fill(nint target, int length)
    {
        if (_memory != null)
        {
            CameraRing.SetReader(_memory, active: true);
            Decode();

            // The phone stopped: back to the placeholder rather than a frozen last picture.
            if (_hasPicture && !CameraRing.WriterActive(_memory, _stale))
            {
                Placeholder(_latest, _width, _height);
                _hasPicture = false;
            }
        }

        new ReadOnlySpan<byte>(_latest, 0, Math.Min(length, _latest.Length)).CopyTo(new Span<byte>((void*)target, length));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();

        if (_memory != null)
        {
            UnmapViewOfFile((nint)_memory);
            _memory = null;
        }

        if (_mapping != 0)
        {
            CloseHandle(_mapping);
            _mapping = 0;
        }
    }

    // The media source runs as a service and may create Global names; the signed-in user (Fenpo.exe) may write.
    private void Open()
    {
        if (_memory != null)
        {
            return;
        }

        const string access = "D:(A;;GA;;;SY)(A;;GA;;;LS)(A;;GA;;;BA)(A;;GRGW;;;IU)";

        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(access, 1, out var descriptor, 0))
        {
            ComServer.Log($"Security descriptor failed: {Marshal.GetLastPInvokeError()}");
            return;
        }

        try
        {
            var attributes = new SecurityAttributes { Length = sizeof(SecurityAttributes), Descriptor = descriptor };
            _mapping = CreateFileMappingW(-1, &attributes, 0x04, (uint)(CameraRing.Size >> 32), (uint)CameraRing.Size, CameraRing.Name);

            if (_mapping == 0)
            {
                ComServer.Log($"Creating the shared memory failed: {Marshal.GetLastPInvokeError()}");
                return;
            }

            _memory = (byte*)MapViewOfFile(_mapping, 0xF001F, 0, 0, (nuint)CameraRing.Size);

            if (_memory == null)
            {
                ComServer.Log($"Mapping the shared memory failed: {Marshal.GetLastPInvokeError()}");
                return;
            }

            CameraRing.Initialize(_memory);
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private void Decode()
    {
        while (CameraRing.TryRead(_memory, _last, _packet, out var frame))
        {
            // A jump means frames were skipped: the decoder starts again at the key frame.
            if (frame.Sequence != _last + 1 && _decoder is not null)
            {
                _decoder.Object.ProcessMessage(MFT_MESSAGE_TYPE.MFT_MESSAGE_COMMAND_FLUSH, 0);
            }

            _last = frame.Sequence;

            if (_decoder is null && !frame.KeyFrame)
            {
                continue;
            }

            try
            {
                EnsureDecoder();
                Feed(frame);
                Drain();
            }
            catch (Exception exception)
            {
                ComServer.Log($"Decoding failed: {exception.Message}");
                _decoder?.Dispose();
                _decoder = null;
            }
        }
    }

    private void EnsureDecoder()
    {
        if (_decoder is not null)
        {
            return;
        }

        var decoder = ComObject.CoCreate<IMFTransform>(Constants.CLSID_MSH264DecoderMFT) ?? throw new InvalidOperationException("No H.264 decoder");

        if (decoder.Object.GetAttributes(out var attributes).IsSuccess && attributes is not null)
        {
            attributes.SetUINT32(Constants.MF_LOW_LATENCY, 1);
        }

        Functions.MFCreateMediaType(out var inputObject).ThrowOnError();
        using var input = new ComObject<IMFMediaType>(inputObject);
        input.Set(Constants.MF_MT_MAJOR_TYPE, Constants.MFMediaType_Video);
        input.Set(Constants.MF_MT_SUBTYPE, Constants.MFVideoFormat_H264);
        input.SetSize(Constants.MF_MT_FRAME_SIZE, (uint)_width, (uint)_height);
        input.SetRatio(Constants.MF_MT_FRAME_RATE, 30, 1);
        decoder.Object.SetInputType(0, input.Object, 0).ThrowOnError();
        _decoder = decoder;
        SetOutputType();
    }

    // NV12 out; the size comes from the stream (a 1080p stream decodes as 1088 lines with a crop).
    private void SetOutputType()
    {
        for (uint index = 0; _decoder!.Object.GetOutputAvailableType(0, index, out var typeObject).IsSuccess; index++)
        {
            using var type = new ComObject<IMFMediaType>(typeObject);

            if (type.Object.GetGUID(Constants.MF_MT_SUBTYPE, out var subtype).IsSuccess && subtype == Constants.MFVideoFormat_NV12)
            {
                _decoder.Object.SetOutputType(0, type.Object, 0).ThrowOnError();
                type.Object.GetUINT64(Constants.MF_MT_FRAME_SIZE, out var size).ThrowOnError();
                _decodedWidth = (int)(size >> 32);
                _decodedHeight = (int)(size & 0xFFFFFFFF);
                _decodedStride = type.Object.GetUINT32(Constants.MF_MT_DEFAULT_STRIDE, out var stride).IsSuccess ? (int)stride : _decodedWidth;
                return;
            }
        }

        throw new InvalidOperationException("The decoder offers no NV12 output");
    }

    private void Feed(CameraRing.FrameInfo frame)
    {
        Functions.MFCreateMemoryBuffer((uint)frame.Length, out var bufferObject).ThrowOnError();
        using var buffer = new ComObject<IMFMediaBuffer>(bufferObject);
        buffer.WithLock((data, max, _) => new ReadOnlySpan<byte>(_packet, 0, frame.Length).CopyTo(new Span<byte>((void*)data, frame.Length)));
        buffer.Object.SetCurrentLength((uint)frame.Length).ThrowOnError();
        Functions.MFCreateSample(out var sampleObject).ThrowOnError();
        using var sample = new ComObject<IMFSample>(sampleObject);
        sample.Object.AddBuffer(buffer.Object).ThrowOnError();
        sample.Object.SetSampleTime(frame.PtsMicroseconds * 10).ThrowOnError();
        _decoder!.Object.ProcessInput(0, sample.Object, 0).ThrowOnError();
    }

    private void Drain()
    {
        while (true)
        {
            _decoder!.Object.GetOutputStreamInfo(0, out var info).ThrowOnError();
            Functions.MFCreateMemoryBuffer(Math.Max(info.cbSize, (uint)(_decodedStride * _decodedHeight * 3 / 2)), out var bufferObject).ThrowOnError();
            using var buffer = new ComObject<IMFMediaBuffer>(bufferObject);
            Functions.MFCreateSample(out var sampleObject).ThrowOnError();
            using var sample = new ComObject<IMFSample>(sampleObject);
            sample.Object.AddBuffer(buffer.Object).ThrowOnError();

            var output = new MFT_OUTPUT_DATA_BUFFER[1];
            HRESULT result = Constants.S_OK;
            ComObject.WithComInstance(sample, unknown =>
            {
                output[0].pSample = unknown;
                result = _decoder.Object.ProcessOutput(0, 1, output, out _);
            });

            if (result == Constants.MF_E_TRANSFORM_NEED_MORE_INPUT)
            {
                return;
            }

            if (result == Constants.MF_E_TRANSFORM_STREAM_CHANGE)
            {
                SetOutputType();
                continue;
            }

            result.ThrowOnError();
            buffer.WithLock((data, max, current) => Copy((byte*)data));
            _hasPicture = true;
        }
    }

    // Decoded NV12 (decoder's size and stride) into the camera's frame, centred: a larger picture is cropped, a
    // smaller one gets dark borders.
    private void Copy(byte* source)
    {
        var copyWidth = Math.Min(_width, _decodedWidth) & ~1;
        var copyHeight = Math.Min(_height, _decodedHeight) & ~1;
        var sourceX = ((_decodedWidth - copyWidth) / 2) & ~1;
        var sourceY = ((_decodedHeight - copyHeight) / 2) & ~1;
        var targetX = ((_width - copyWidth) / 2) & ~1;
        var targetY = ((_height - copyHeight) / 2) & ~1;

        if (copyWidth < _width || copyHeight < _height)
        {
            Placeholder(_latest, _width, _height);
        }

        fixed (byte* target = _latest)
        {
            for (var row = 0; row < copyHeight; row++)
            {
                Buffer.MemoryCopy(source + ((sourceY + row) * _decodedStride) + sourceX, target + ((targetY + row) * _width) + targetX, copyWidth, copyWidth);
            }

            var sourceChroma = source + (_decodedStride * _decodedHeight);
            var targetChroma = target + (_width * _height);

            for (var row = 0; row < copyHeight / 2; row++)
            {
                Buffer.MemoryCopy(sourceChroma + (((sourceY / 2) + row) * _decodedStride) + sourceX, targetChroma + (((targetY / 2) + row) * _width) + targetX, copyWidth, copyWidth);
            }
        }
    }

    private static byte[] Placeholder(int width, int height)
    {
        var frame = new byte[width * height * 3 / 2];
        Placeholder(frame, width, height);
        return frame;
    }

    // Fenpo's ink colour (#1B1F3B) in NV12: dark, slightly blue.
    private static void Placeholder(byte[] frame, int width, int height)
    {
        frame.AsSpan(0, width * height).Fill(32);

        for (var index = width * height; index + 1 < frame.Length; index += 2)
        {
            frame[index] = 140;
            frame[index + 1] = 124;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        public int Inherit;
    }

    [LibraryImport("advapi32", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string descriptor, uint revision, out nint result, nint size);

    [LibraryImport("kernel32", EntryPoint = "CreateFileMappingW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateFileMappingW(nint file, SecurityAttributes* attributes, uint protect, uint sizeHigh, uint sizeLow, string name);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint MapViewOfFile(nint mapping, uint access, uint offsetHigh, uint offsetLow, nuint bytes);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnmapViewOfFile(nint address);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32")]
    private static partial nint LocalFree(nint memory);
}
