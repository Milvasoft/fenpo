using System.Threading.Channels;
using Fenpo.Core.Channel;
using Fenpo.Protocol.V1;
using Fenpo.Transport;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// An open media stream from the phone: what it said it sends and its packets as they arrive.
/// </summary>
/// <param name="Open">The stream's first frame.</param>
/// <param name="Packets">Packets in arrival order; completes when the phone closes the stream.</param>
/// <param name="Audio">The stream's sound packets, apart from the picture's; completes with <paramref name="Packets"/>.</param>
public sealed record MediaFeed(MediaStreamOpen Open, ChannelReader<StreamPacket> Packets, ChannelReader<StreamPacket> Audio);

/// <summary>
/// Takes the phone's media connections (video.proto) from the control server, reads their packets and hands the
/// stream to the screen mirror or the camera by its kind.
/// </summary>
public sealed class MediaStreamRouter(ControlServer server, ScreenMirrorService screen, CameraService camera, ILogger<MediaStreamRouter> logger) : IMediaStreamConnections, IHostedService
{
    // A backlog this long means the player is not keeping up; older video is dropped until the next key frame.
    private const int _maxQueued = 90;

    private readonly ControlServer _server = server;
    private readonly ScreenMirrorService _screen = screen;
    private readonly CameraService _camera = camera;
    private readonly ILogger<MediaStreamRouter> _logger = logger;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _server.UseMediaStreams(this);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task HandleAsync(PairedAppDevice device, MediaStreamOpen open, Stream stream, CancellationToken cancellationToken)
    {
        _logger.LogInformation("{Kind} stream from {Phone}: {Codec} {Width}x{Height} at {Fps} fps", open.Kind, device.Name, open.Video?.Codec, open.Video?.Width, open.Video?.Height, open.Video?.Fps);
        // Not single-reader: that variant cannot count its backlog, which the drop below needs.
        var packets = Channel.CreateUnbounded<StreamPacket>(new UnboundedChannelOptions { SingleWriter = true });

        // Sound has its own player; about a second of it is kept, the oldest dropped, so a feed nobody plays does not grow.
        var audio = Channel.CreateBounded<StreamPacket>(new BoundedChannelOptions(100) { SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
        var feed = new MediaFeed(open, packets.Reader, audio.Reader);

        try
        {
            switch (open.Kind)
            {
                case StreamKind.Screen:
                    await _screen.AcceptAsync(feed);
                    break;
                case StreamKind.Camera:
                    await _camera.AcceptAsync(feed);
                    break;
                default:
                    return;
            }

            var traffic = new Traffic(open.Kind, _logger);

            while (await EnvelopeFraming.ReadFrameAsync(stream, cancellationToken) is { } frame)
            {
                var packet = StreamPacket.Parser.ParseFrom(frame);
                var dropped = packets.Reader.Count > _maxQueued && !packet.KeyFrame && !packet.CodecConfig;
                traffic.Add(frame.Length, packets.Reader.Count, dropped);

                if (packet.Track == StreamTrack.Audio)
                {
                    audio.Writer.TryWrite(packet);
                }
                else if (!dropped)
                {
                    packets.Writer.TryWrite(packet);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidProtocolBufferException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogInformation("{Kind} stream ended: {Reason}", open.Kind, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "{Kind} stream broke", open.Kind);
        }
        finally
        {
            packets.Writer.TryComplete();
            audio.Writer.TryComplete();
            _logger.LogInformation("{Kind} stream from {Phone} closed", open.Kind, device.Name);
        }
    }

    // What arrives from the phone, every few seconds, so a frozen picture can be told apart: nothing coming in (the
    // phone stopped) or packets piling up (the PC stopped taking them).
    private sealed class Traffic(StreamKind kind, ILogger logger)
    {
        private readonly StreamKind _kind = kind;
        private readonly ILogger _logger = logger;
        private readonly System.Diagnostics.Stopwatch _window = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _sinceLast = System.Diagnostics.Stopwatch.StartNew();
        private int _packets;
        private long _bytes;
        private int _dropped;

        public void Add(int bytes, int queued, bool dropped)
        {
            if (_sinceLast.Elapsed > TimeSpan.FromSeconds(1))
            {
                _logger.LogInformation("{Kind} stream: nothing came from the phone for {Silence} ms", _kind, (int)_sinceLast.Elapsed.TotalMilliseconds);
            }

            _sinceLast.Restart();
            _packets++;
            _bytes += bytes;
            _dropped += dropped ? 1 : 0;

            if (_window.Elapsed >= TimeSpan.FromSeconds(3))
            {
                var seconds = _window.Elapsed.TotalSeconds;
                _logger.LogInformation("{Kind} stream: {Rate:F1} packets/s in, {Kbps} kbit/s, {Queued} waiting, {Dropped} dropped", _kind, _packets / seconds, (int)(_bytes * 8 / 1000 / seconds), queued, _dropped);
                _packets = 0;
                _bytes = 0;
                _dropped = 0;
                _window.Restart();
            }
        }
    }
}
