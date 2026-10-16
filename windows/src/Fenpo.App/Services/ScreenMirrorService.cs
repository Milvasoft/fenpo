using System.Threading.Channels;
using Fenpo.Core.Channel;
using Fenpo.Protocol.V1;
using Fenpo.Transport;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// An open screen stream: what the phone said it sends and its packets as they arrive.
/// </summary>
/// <param name="Open">The stream's first frame.</param>
/// <param name="Packets">Packets in arrival order; completes when the phone closes the stream.</param>
public sealed record MirrorStream(MediaStreamOpen Open, ChannelReader<StreamPacket> Packets);

/// <summary>
/// The Android phone's screen on the PC (CAPABILITY_SCREEN_MIRROR, screen.proto): asks the phone to start, follows
/// ScreenStatus, takes the media connection the phone opens and passes its packets to the mirror window, and sends
/// touches, swipes and the global buttons back as ScreenInput.
/// </summary>
public sealed class ScreenMirrorService(PhoneChannel channel, ControlServer server, ILogger<ScreenMirrorService> logger) : IHostedService, IMediaStreamConnections
{
    // A backlog this long means the window is not keeping up; older video is dropped until the next key frame.
    private const int _maxQueued = 90;

    private readonly PhoneChannel _channel = channel;
    private readonly ControlServer _server = server;
    private readonly ILogger<ScreenMirrorService> _logger = logger;
    private string? _streamId;

    /// <summary>
    /// Raised from a background thread when the state, size or control availability changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised from a background thread when the phone opened the media connection.
    /// </summary>
    public event EventHandler<MirrorStream>? StreamOpened;

    /// <summary>
    /// Gets the latest state the phone reported.
    /// </summary>
    public ScreenState State { get; private set; } = ScreenState.Stopped;

    /// <summary>
    /// Gets the size and codec as encoded (changes when the phone rotates), or null before streaming.
    /// </summary>
    public VideoFormat? Video { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the phone accepts touches (Fenpo's accessibility service is on).
    /// </summary>
    public bool ControlAvailable { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the phone in use can be mirrored.
    /// </summary>
    public bool CanMirror => _channel.IsConnected && _channel.Supports(Capability.ScreenMirror);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _server.UseMediaStreams(this);
        _channel.MessageReceived += (sender, message) => OnMessage(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Asks the phone to share its screen; it asks its user first.
    /// </summary>
    /// <returns>False when the phone could not be reached.</returns>
    public async Task<bool> StartMirroringAsync()
    {
        if (!CanMirror)
        {
            return false;
        }

        var start = new ScreenStart
        {
            Max = new VideoFormat { Codec = VideoCodec.H264, Width = 1920, Height = 1920, Fps = 60, BitrateKbps = 8000 },
            Control = true,
            Accepted = new AcceptedFormats { Video = { VideoCodec.H264 } },

            // The phone's sound needs a second track in the player; video comes first.
            Audio = false,
        };
        SetState(ScreenState.WaitingForConsent);
        var sent = await _channel.SendAsync(new Envelope { ScreenStart = start });
        _logger.LogInformation("Screen mirroring asked: {Sent}", sent);

        if (!sent)
        {
            SetState(ScreenState.Failed);
        }

        return sent;
    }

    /// <summary>
    /// Stops mirroring on the phone.
    /// </summary>
    /// <returns>A task that completes when the phone was told.</returns>
    public async Task StopMirroringAsync()
    {
        if (State is ScreenState.Stopped)
        {
            return;
        }

        await _channel.SendAsync(new Envelope { ScreenStop = new ScreenStop() });
        SetState(ScreenState.Stopped);
    }

    /// <summary>
    /// Sends a touch, swipe, global button or text to the phone.
    /// </summary>
    /// <param name="input">Input.</param>
    /// <returns>False when the phone could not be reached.</returns>
    public Task<bool> SendInputAsync(ScreenInput input) => _channel.SendAsync(new Envelope { ScreenInput = input });

    /// <summary>
    /// Asks the phone for a key frame, so the picture can start or recover without waiting for the next one.
    /// </summary>
    /// <returns>A task that completes when sent.</returns>
    public Task RequestKeyFrameAsync() => _streamId is { } id ? _channel.SendAsync(new Envelope { RequestKeyFrame = new RequestKeyFrame { StreamId = id } }) : Task.CompletedTask;

    /// <inheritdoc/>
    public async Task HandleAsync(PairedAppDevice device, MediaStreamOpen open, Stream stream, CancellationToken cancellationToken)
    {
        if (open.Kind != StreamKind.Screen)
        {
            _logger.LogInformation("{Kind} stream from {Phone} closed: not handled yet", open.Kind, device.Name);
            return;
        }

        _streamId = open.StreamId;
        Video = open.Video;
        _logger.LogInformation("Screen stream from {Phone}: {Codec} {Width}x{Height} at {Fps} fps", device.Name, open.Video?.Codec, open.Video?.Width, open.Video?.Height, open.Video?.Fps);
        var packets = Channel.CreateUnbounded<StreamPacket>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        StreamOpened?.Invoke(this, new MirrorStream(open, packets.Reader));
        await RequestKeyFrameAsync();

        try
        {
            while (await EnvelopeFraming.ReadFrameAsync(stream, cancellationToken) is { } frame)
            {
                var packet = StreamPacket.Parser.ParseFrom(frame);

                // Falling behind: drop queued video, the decoder restarts at the next key frame.
                if (packets.Reader.Count > _maxQueued && !packet.KeyFrame)
                {
                    continue;
                }

                packets.Writer.TryWrite(packet);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidProtocolBufferException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogInformation("Screen stream ended: {Reason}", exception.Message);
        }
        finally
        {
            packets.Writer.TryComplete();
            _logger.LogInformation("Screen stream from {Phone} closed", device.Name);
        }
    }

    private void OnMessage(ChannelMessage message)
    {
        if (message.Envelope.BodyCase == Envelope.BodyOneofCase.ScreenStatus)
        {
            var status = message.Envelope.ScreenStatus;
            _streamId = string.IsNullOrEmpty(status.StreamId) ? _streamId : status.StreamId;
            Video = status.Video ?? Video;
            ControlAvailable = status.ControlAvailable;
            _logger.LogInformation("Screen status {State} ({Width}x{Height}, control {Control}) {Reason}", status.State, status.Video?.Width, status.Video?.Height, status.ControlAvailable, status.Reason);
            SetState(status.State);
        }
        else if (message.Envelope.BodyCase == Envelope.BodyOneofCase.ScreenStop)
        {
            SetState(ScreenState.Stopped);
        }
    }

    private void SetState(ScreenState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
