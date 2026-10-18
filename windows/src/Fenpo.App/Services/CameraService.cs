using Fenpo.Core.Settings;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// The phone's camera (CAPABILITY_CAMERA, camera.proto): starts and stops it, keeps the lens choice in step both
/// ways (the Camera page and changes made on the phone), and takes the camera stream from
/// <see cref="MediaStreamRouter"/> for the preview. Center Stage, portrait blur and the phone's microphone are not
/// offered (removed 17 Oct: they did not work reliably), so both are always sent off.
/// </summary>
public sealed class CameraService(PhoneChannel channel, ISettingsStore settings, ILogger<CameraService> logger) : IHostedService
{
    private readonly PhoneChannel _channel = channel;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<CameraService> _logger = logger;
    private string? _streamId;
    private CameraSettings? _sent;
    private bool _adopting;

    /// <summary>
    /// Raised from a background thread when the state or what the camera offers changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised from a background thread when the phone opened the camera stream.
    /// </summary>
    public event EventHandler<MediaFeed>? StreamOpened;

    /// <summary>
    /// Gets the latest state the phone reported.
    /// </summary>
    public CameraState State { get; private set; } = CameraState.Stopped;

    /// <summary>
    /// Gets the size and codec as encoded, or null before streaming.
    /// </summary>
    public VideoFormat? Video { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last start failed because Fenpo on the phone does not offer the camera
    /// (an older version).
    /// </summary>
    public bool Unsupported { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the phone in use has a camera Fenpo can use.
    /// </summary>
    public bool CanUseCamera => _channel.IsConnected && _channel.Supports(Capability.Camera);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => OnMessage(message);
        _settings.Changed += (sender, current) => _ = OnSettingsChangedAsync(current);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    // Closing Fenpo is a stop, not a lost connection: the phone says so instead of reporting a failure.
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopCameraAsync().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

    /// <summary>
    /// Asks the phone to start its camera for an app; Android first asks for a tap on the phone.
    /// </summary>
    /// <param name="consumer">App using the camera, shown on the phone.</param>
    /// <returns>False when the phone could not be reached.</returns>
    public async Task<bool> StartCameraAsync(string consumer)
    {
        if (!CanUseCamera)
        {
            _logger.LogInformation("Camera not asked: phone connected {Connected}, offers the camera {Offers}", _channel.IsConnected, _channel.Supports(Capability.Camera));
            SetState(CameraState.Failed);
            Unsupported = _channel.IsConnected;
            return false;
        }

        Unsupported = false;
        _sent = ToWire(_settings.Current);
        var start = new CameraStart
        {
            Settings = _sent,
            Video = new VideoFormat { Codec = VideoCodec.H264, Width = 1920, Height = 1080, Fps = 30, BitrateKbps = 6000 },
            Consumer = consumer,

            // No audio until the PC has a virtual microphone to put it in.
            Accepted = new AcceptedFormats { Video = { VideoCodec.H264 } },
        };
        SetState(CameraState.Starting);
        var sent = await _channel.SendAsync(new Envelope { CameraStart = start });
        _logger.LogInformation("Camera asked for {Consumer}: {Sent}", consumer, sent);

        if (!sent)
        {
            SetState(CameraState.Failed);
        }

        return sent;
    }

    /// <summary>
    /// Stops the phone's camera.
    /// </summary>
    /// <returns>A task that completes when the phone was told.</returns>
    public async Task StopCameraAsync()
    {
        if (State is CameraState.Stopped)
        {
            return;
        }

        await _channel.SendAsync(new Envelope { CameraStop = new CameraStop() });
        SetState(CameraState.Stopped);
    }

    /// <summary>
    /// Asks the phone for a key frame, so the picture can start or recover without waiting for the next one.
    /// </summary>
    /// <returns>A task that completes when sent.</returns>
    public Task RequestKeyFrameAsync() => _streamId is { } id ? _channel.SendAsync(new Envelope { RequestKeyFrame = new RequestKeyFrame { StreamId = id } }) : Task.CompletedTask;

    /// <summary>
    /// Takes the camera stream the phone opened.
    /// </summary>
    /// <param name="feed">Stream.</param>
    /// <returns>A task that completes when the phone was asked for a key frame.</returns>
    public async Task AcceptAsync(MediaFeed feed)
    {
        _streamId = feed.Open.StreamId;
        Video = feed.Open.Video;
        StreamOpened?.Invoke(this, feed);
        await RequestKeyFrameAsync();
    }

    // Lens choices on the PC: wide (back, 1×), ultra wide (back, 0.5×), front.
    private static CameraSettings ToWire(AppSettings settings) => new()
    {
        Facing = settings.CameraLens == CameraLens.Front ? CameraFacing.Front : CameraFacing.Back,
        Zoom = settings.CameraLens == CameraLens.UltraWide ? 0.5f : 1f,
        Microphone = false,
        CenterStage = false,
    };

    private static CameraLens LensOf(CameraSettings settings) => settings.Facing == CameraFacing.Front ? CameraLens.Front : settings.Zoom is > 0 and < 1 ? CameraLens.UltraWide : CameraLens.Wide;

    // A change on the Camera page goes to the phone while the camera runs.
    private async Task OnSettingsChangedAsync(AppSettings current)
    {
        // A change that came from the phone is not sent back to it.
        if (_adopting)
        {
            return;
        }

        var wanted = ToWire(current);

        if (State is not (CameraState.Starting or CameraState.Streaming or CameraState.WaitingForTap) || wanted.Equals(_sent))
        {
            return;
        }

        _sent = wanted;
        await _channel.SendAsync(new Envelope { CameraSettings = new CameraSettingsChange { Settings = wanted } });
    }

    private void OnMessage(ChannelMessage message)
    {
        switch (message.Envelope.BodyCase)
        {
            case Envelope.BodyOneofCase.CameraStatus:
                var status = message.Envelope.CameraStatus;
                _streamId = string.IsNullOrEmpty(status.StreamId) ? _streamId : status.StreamId;
                Video = status.Video ?? Video;
                _logger.LogInformation("Camera status {State} ({Width}x{Height}) {Reason}", status.State, status.Video?.Width, status.Video?.Height, status.Reason);

                if (status.Settings is { } applied)
                {
                    Adopt(applied);
                }

                SetState(status.State);
                break;
            case Envelope.BodyOneofCase.CameraSettings:
                // Changed on the phone's screen: the Camera page follows.
                Adopt(message.Envelope.CameraSettings.Settings);
                break;
            case Envelope.BodyOneofCase.CameraStop:
                SetState(CameraState.Stopped);
                break;
        }
    }

    private void Adopt(CameraSettings applied)
    {
        _adopting = true;

        try
        {
            _settings.Update(s => s with { CameraLens = LensOf(applied) });
        }
        finally
        {
            _adopting = false;
        }

        // What the phone applied, in the PC's own terms, so the next real change is compared against it.
        _sent = ToWire(_settings.Current);
    }

    private void SetState(CameraState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
