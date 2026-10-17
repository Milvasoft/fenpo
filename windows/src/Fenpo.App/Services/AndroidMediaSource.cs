using Fenpo.Core.Media;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Command = Fenpo.Core.Media.MediaCommand;
using WireCommand = Fenpo.Protocol.V1.MediaCommand;

namespace Fenpo.App.Services;

/// <summary>
/// What an Android phone plays, from MediaState over the Fenpo channel (CAPABILITY_MEDIA, media.proto), as an
/// <see cref="IMediaSource"/> next to AMS. Commands go back as MediaCommand.
/// </summary>
public sealed class AndroidMediaSource(PhoneChannel channel, ILogger<AndroidMediaSource> logger) : IMediaSource, IHostedService
{
    private const uint _volumeStep = 10;

    // Clocks of the phone and the PC may differ; a report far from the PC's clock is taken as "now".
    private static readonly TimeSpan _clockTolerance = TimeSpan.FromSeconds(10);

    private readonly PhoneChannel _channel = channel;
    private readonly ILogger<AndroidMediaSource> _logger = logger;
    private NowPlaying _current = NowPlaying.Empty;
    private uint _volume;

    /// <inheritdoc/>
    public event EventHandler<NowPlaying>? Changed;

    /// <inheritdoc/>
    public NowPlaying Current => Volatile.Read(ref _current);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => OnMessage(message);
        _channel.Changed += (sender, e) =>
        {
            if (!_channel.AndroidConnected && Current.HasTrack)
            {
                Publish(NowPlaying.Empty);
            }
        };
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task<bool> SendAsync(Command command, CancellationToken cancellationToken = default)
    {
        var wire = command switch
        {
            Command.Play => new WireCommand { Action = MediaAction.Play },
            Command.Pause => new WireCommand { Action = MediaAction.Pause },
            Command.TogglePlayPause => new WireCommand { Action = MediaAction.Toggle },
            Command.NextTrack => new WireCommand { Action = MediaAction.Next },
            Command.PreviousTrack => new WireCommand { Action = MediaAction.Previous },
            Command.VolumeUp => new WireCommand { Action = MediaAction.SetVolume, VolumePercent = Math.Min(100, _volume + _volumeStep) },
            Command.VolumeDown => new WireCommand { Action = MediaAction.SetVolume, VolumePercent = _volume > _volumeStep ? _volume - _volumeStep : 0 },
            _ => null,
        };

        if (wire is null)
        {
            return false;
        }

        return await _channel.SendToAndroidAsync(new Envelope { MediaCommand = wire }, cancellationToken);
    }

    /// <summary>
    /// Turns a MediaState into what the PC shows.
    /// </summary>
    /// <param name="state">Phone's report.</param>
    /// <param name="now">PC time.</param>
    /// <returns>Now playing.</returns>
    public static NowPlaying ToNowPlaying(MediaState state, DateTimeOffset now)
    {
        if (!state.Active)
        {
            return NowPlaying.Empty;
        }

        var reportedAt = state.PositionAt?.ToDateTimeOffset() ?? now;

        if ((reportedAt - now).Duration() > _clockTolerance)
        {
            reportedAt = now;
        }

        var commands = new HashSet<Command> { Command.Play, Command.Pause, Command.TogglePlayPause, Command.VolumeUp, Command.VolumeDown };

        if (state.CanNext)
        {
            commands.Add(Command.NextTrack);
        }

        if (state.CanPrevious)
        {
            commands.Add(Command.PreviousTrack);
        }

        return new NowPlaying
        {
            PlayerName = state.AppName,
            Title = state.Title,
            Artist = state.Artist,
            Album = state.Album,
            Duration = state.DurationMs > 0 ? TimeSpan.FromMilliseconds(state.DurationMs) : null,
            Elapsed = TimeSpan.FromMilliseconds(state.PositionMs),
            ElapsedAt = reportedAt,
            Rate = state.Playing ? 1 : 0,
            State = state.Playing ? PlaybackState.Playing : PlaybackState.Paused,
            Volume = state.VolumePercent / 100.0,
            SupportedCommands = commands,
        };
    }

    private void OnMessage(ChannelMessage message)
    {
        switch (message.Envelope.BodyCase)
        {
            // The iPhone's player comes through AMS; a MediaState here is only ever Android's.
            case Envelope.BodyOneofCase.MediaState when message.Device.Platform != DevicePlatform.Ios:
                _volume = message.Envelope.MediaState.VolumePercent;
                Publish(ToNowPlaying(message.Envelope.MediaState, DateTimeOffset.Now));
                break;
        }
    }

    private void Publish(NowPlaying nowPlaying)
    {
        var previous = Interlocked.Exchange(ref _current, nowPlaying);

        if (previous.Title != nowPlaying.Title || previous.State != nowPlaying.State)
        {
            _logger.LogInformation("Android media: {Player} {State}, {Length} character title", nowPlaying.PlayerName, nowPlaying.State, nowPlaying.Title.Length);
        }

        Changed?.Invoke(this, nowPlaying);
    }
}
