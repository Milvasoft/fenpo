using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// The Android phone's screen on the PC (CAPABILITY_SCREEN_MIRROR, screen.proto): asks the phone to start, follows
/// ScreenStatus, takes the screen stream from <see cref="MediaStreamRouter"/> for the mirror window, and sends touches,
/// swipes and the global buttons back as ScreenInput.
/// </summary>
public sealed class ScreenMirrorService(PhoneChannel channel, ILogger<ScreenMirrorService> logger) : IHostedService
{
    private readonly PhoneChannel _channel = channel;
    private readonly ILogger<ScreenMirrorService> _logger = logger;
    private string? _streamId;

    /// <summary>
    /// Raised from a background thread when the state, size or control availability changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised from a background thread when the phone opened the screen stream.
    /// </summary>
    public event EventHandler<MediaFeed>? StreamOpened;

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

    /// <summary>
    /// Gets a value indicating whether the phone in use is an iPhone: its user starts the broadcast on the phone, and
    /// the PC cannot control it (screen.proto).
    /// </summary>
    public bool FromIphone => _channel.Phone?.Platform == DevicePlatform.Ios;

    /// <summary>
    /// Gets the stream playing now, for a window that opens after it started (an iPhone broadcast started on the phone).
    /// </summary>
    public MediaFeed? Feed { get; private set; }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => OnMessage(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    // Closing Fenpo is a stop, not a lost connection.
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopMirroringAsync().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

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
            // Sound uncompressed: the PC plays PCM without a decoder, and a LAN carries it easily.
            Accepted = new AcceptedFormats { Video = { VideoCodec.H264 }, Audio = { AudioCodec.PcmS16Le } },
            Audio = true,
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

    /// <summary>
    /// Takes the screen stream the phone opened.
    /// </summary>
    /// <param name="feed">Stream.</param>
    /// <returns>A task that completes when the phone was asked for a key frame.</returns>
    public async Task AcceptAsync(MediaFeed feed)
    {
        _streamId = feed.Open.StreamId;
        Video = feed.Open.Video;
        Feed = feed;
        StreamOpened?.Invoke(this, feed);
        await RequestKeyFrameAsync();
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
