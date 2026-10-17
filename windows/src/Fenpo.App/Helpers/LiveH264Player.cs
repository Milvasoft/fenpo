using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DirectN;
using DirectN.Extensions;
using DirectN.Extensions.Com;
using DirectN.Extensions.Utilities;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Logging;
using ComObject = DirectN.Extensions.Com.ComObject;

namespace Fenpo.App.Helpers;

/// <summary>
/// Plays a live H.264 stream from the phone (video.proto): Windows' H.264 decoder (the same one Fenpo Kamera uses)
/// turns every frame into a picture as soon as it arrives, and <see cref="FrameReady"/> hands it to
/// <see cref="Controls.LiveVideoView"/>. Windows' media player was dropped: after a few seconds it showed only the key
/// frames of a stream that came in evenly at 30 frames a second. Used by the screen mirror window and the camera preview.
/// </summary>
public sealed class LiveH264Player : IDisposable
{
    // Frames go in but no picture has come out for this long: the decoder lost its reference (the phone switched
    // lenses or paused) and waits for a key frame; the phone is asked for one at once.
    private static readonly TimeSpan Stall = TimeSpan.FromMilliseconds(500);

    private static int _mediaFoundation;

    private readonly ChannelReader<StreamPacket> _packets;
    private readonly ILogger _logger;
    private readonly Action _keyFrameNeeded;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly Stopwatch _sinceShown = Stopwatch.StartNew();
    private readonly Stopwatch _sinceKeyFrameAsked = Stopwatch.StartNew();
    private readonly Stopwatch _window = Stopwatch.StartNew();
    private IComObject<IMFTransform>? _decoder;
    private byte[] _config = [];
    private byte[] _picture = [];
    private int _decodedWidth;
    private int _decodedHeight;
    private int _decodedStride;
    private int _framesIn;
    private int _picturesOut;
    private int _keyFrames;
    private TimeSpan _longestGap;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveH264Player"/> class and starts playing.
    /// </summary>
    /// <param name="packets">The stream's packets; only video is used.</param>
    /// <param name="width">Encoded width.</param>
    /// <param name="height">Encoded height.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="keyFrameNeeded">Called when the picture cannot go on without a key frame (ask the phone for one).</param>
    public LiveH264Player(ChannelReader<StreamPacket> packets, uint width, uint height, ILogger logger, Action keyFrameNeeded)
    {
        _packets = packets;
        _logger = logger;
        _keyFrameNeeded = keyFrameNeeded;
        Size = (width == 0 ? 1280 : width, height == 0 ? 720 : height);

        if (Interlocked.Exchange(ref _mediaFoundation, 1) == 0)
        {
            Functions.MFStartup(0x00020070, 1).ThrowOnError(); // MF_VERSION, MFSTARTUP_LITE
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Fenpo live video" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>
    /// Receives a decoded picture.
    /// </summary>
    /// <param name="bgra">The picture, valid only during the call.</param>
    public delegate void FrameHandler(ReadOnlySpan<byte> bgra);

    /// <summary>
    /// A picture is ready, on the decoding thread: BGRA at <see cref="Size"/>, 4 bytes a pixel, rows without padding.
    /// The span is valid only during the call.
    /// </summary>
    public event FrameHandler? FrameReady;

    /// <summary>
    /// Gets the size the stream was announced with; pictures have this size.
    /// </summary>
    public (uint Width, uint Height) Size { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        try
        {
            RunAsync().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
        finally
        {
            _decoder?.Dispose();
            _decoder = null;
        }
    }

    private async Task RunAsync()
    {
        var cancellationToken = _stop.Token;

        while (await _packets.WaitToReadAsync(cancellationToken))
        {
            while (!cancellationToken.IsCancellationRequested && _packets.TryRead(out var packet))
            {
                if (packet.Track != StreamTrack.Video)
                {
                    continue;
                }

                if (packet.CodecConfig)
                {
                    _config = packet.Data.ToByteArray();
                    continue;
                }

                if (_decoder is null && !packet.KeyFrame)
                {
                    continue;
                }

                Decode(packet);
                Measure(packet.KeyFrame);
            }
        }
    }

    private void Decode(StreamPacket packet)
    {
        try
        {
            EnsureDecoder();
            byte[] data = packet.KeyFrame ? [.. _config, .. packet.Data.Span] : packet.Data.ToByteArray();
            Feed(data, (long)packet.PtsUs * 10);
            Drain();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning("Decoding the live stream failed, starting again at the next key frame: {Reason}", exception.Message);
            _decoder?.Dispose();
            _decoder = null;
            AskForKeyFrame();
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
        input.SetSize(Constants.MF_MT_FRAME_SIZE, Size.Width, Size.Height);
        input.SetRatio(Constants.MF_MT_FRAME_RATE, 30, 1);
        decoder.Object.SetInputType(0, input.Object, 0).ThrowOnError();
        _decoder = decoder;
        SetOutputType();
    }

    // NV12 out; the size comes from the stream (a 1080p stream decodes as 1088 lines, the last 8 cropped).
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

    private unsafe void Feed(byte[] data, long time)
    {
        Functions.MFCreateMemoryBuffer((uint)data.Length, out var bufferObject).ThrowOnError();
        using var buffer = new ComObject<IMFMediaBuffer>(bufferObject);
        buffer.WithLock((target, max, _) => data.AsSpan().CopyTo(new Span<byte>((void*)target, data.Length)));
        buffer.Object.SetCurrentLength((uint)data.Length).ThrowOnError();
        Functions.MFCreateSample(out var sampleObject).ThrowOnError();
        using var sample = new ComObject<IMFSample>(sampleObject);
        sample.Object.AddBuffer(buffer.Object).ThrowOnError();
        sample.Object.SetSampleTime(time).ThrowOnError();
        _decoder!.Object.ProcessInput(0, sample.Object, 0).ThrowOnError();
    }

    private unsafe void Drain()
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
            buffer.WithLock((data, max, current) => Show((byte*)data));
        }
    }

    // Decoded NV12 (decoder's size and stride) to BGRA at the announced size, then out to the view.
    private unsafe void Show(byte* nv12)
    {
        var width = (int)Math.Min(Size.Width, (uint)_decodedWidth) & ~1;
        var height = (int)Math.Min(Size.Height, (uint)_decodedHeight) & ~1;
        var length = (int)(Size.Width * Size.Height * 4);

        if (_picture.Length != length)
        {
            _picture = new byte[length];
        }

        fixed (byte* bgra = _picture)
        {
            ToBgra(nv12, _decodedStride, _decodedHeight, bgra, (int)Size.Width * 4, width, height);
        }

        _longestGap = _sinceShown.Elapsed > _longestGap ? _sinceShown.Elapsed : _longestGap;
        _sinceShown.Restart();
        _picturesOut++;
        FrameReady?.Invoke(_picture);
    }

    // BT.709 limited range, the phones' HD default; rows in parallel bands.
    private static unsafe void ToBgra(byte* nv12, int stride, int decodedHeight, byte* bgra, int bgraStride, int width, int height)
    {
        var luma = (nint)nv12;
        var chroma = (nint)(nv12 + (stride * decodedHeight));
        var target = (nint)bgra;
        var bands = Math.Clamp(Environment.ProcessorCount, 1, 8);
        var rowsPerBand = ((height / 2) + bands - 1) / bands * 2;

        Parallel.For(0, bands, band =>
        {
            var first = band * rowsPerBand;
            var last = Math.Min(height, first + rowsPerBand);

            for (var row = first; row < last; row++)
            {
                var y = (byte*)luma + (row * stride);
                var uv = (byte*)chroma + ((row / 2) * stride);
                var output = (byte*)target + (row * bgraStride);

                for (var column = 0; column < width; column += 2)
                {
                    var u = uv[column] - 128;
                    var v = uv[column + 1] - 128;
                    var red = (459 * v) + 128;
                    var green = (-55 * u) - (136 * v) + 128;
                    var blue = (541 * u) + 128;

                    for (var pixel = 0; pixel < 2; pixel++)
                    {
                        var c = (y[column + pixel] - 16) * 298;
                        var o = output + ((column + pixel) * 4);
                        o[0] = Clamp((c + blue) >> 8);
                        o[1] = Clamp((c + green) >> 8);
                        o[2] = Clamp((c + red) >> 8);
                        o[3] = 255;
                    }
                }
            }
        });
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    private void AskForKeyFrame()
    {
        if (_sinceKeyFrameAsked.Elapsed > Stall)
        {
            _sinceKeyFrameAsked.Restart();
            _keyFrameNeeded();
        }
    }

    // Every few seconds: frames in, pictures out, key frames, and the longest wait between two pictures.
    private void Measure(bool keyFrame)
    {
        _framesIn++;
        _keyFrames += keyFrame ? 1 : 0;

        if (keyFrame)
        {
            _sinceKeyFrameAsked.Restart();
        }
        else if (_sinceShown.Elapsed > Stall)
        {
            _logger.LogInformation("Live stream: no picture for {Ms} ms while frames come in; asking the phone for a key frame", (int)_sinceShown.Elapsed.TotalMilliseconds);
            AskForKeyFrame();
        }

        if (_window.Elapsed >= TimeSpan.FromSeconds(3))
        {
            var seconds = _window.Elapsed.TotalSeconds;
            _logger.LogInformation("Live stream: {In:F1} frames/s in, {Out:F1} pictures/s out, {KeyFrames} key frames, {Queued} waiting, longest gap {Gap} ms", _framesIn / seconds, _picturesOut / seconds, _keyFrames, _packets.Count, (int)_longestGap.TotalMilliseconds);
            _framesIn = 0;
            _picturesOut = 0;
            _keyFrames = 0;
            _longestGap = TimeSpan.Zero;
            _window.Restart();
        }
    }
}
