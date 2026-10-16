using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Logging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;

namespace Fenpo.App.Helpers;

/// <summary>
/// Plays a live H.264 stream from the phone (video.proto) with Windows' own decoder: a live
/// <see cref="MediaStreamSource"/> without buffering, starting at a key frame with the SPS/PPS in front, time from the
/// phone's clock relative to the first sample. Used by the screen mirror window and the camera preview.
/// </summary>
public sealed class LiveH264Player : IDisposable
{
    private readonly CancellationTokenSource _feeding = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveH264Player"/> class and starts playing.
    /// </summary>
    /// <param name="packets">The stream's packets; only video is used.</param>
    /// <param name="width">Encoded width.</param>
    /// <param name="height">Encoded height.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="failed">Called when Windows cannot show the stream (ask the phone for a key frame).</param>
    public LiveH264Player(ChannelReader<StreamPacket> packets, uint width, uint height, ILogger logger, Action failed)
    {
        var properties = VideoEncodingProperties.CreateH264();
        properties.Width = width == 0 ? 1280 : width;
        properties.Height = height == 0 ? 720 : height;
        Size = (properties.Width, properties.Height);
        var source = new MediaStreamSource(new VideoStreamDescriptor(properties)) { BufferTime = TimeSpan.Zero, IsLive = true, CanSeek = false };
        var feeder = new SampleFeeder(packets, _feeding.Token, logger, Position);
        source.Starting += (sender, args) => args.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += async (sender, args) =>
        {
            var deferral = args.Request.GetDeferral();

            try
            {
                args.Request.Sample = await feeder.NextAsync();
            }
            catch (OperationCanceledException)
            {
                args.Request.Sample = null;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Feeding the live stream failed");
                args.Request.Sample = null;
            }
            finally
            {
                deferral.Complete();
            }
        };

        Player = new MediaPlayer { RealTimePlayback = true, IsMuted = true };
        Player.MediaFailed += (sender, args) =>
        {
            logger.LogWarning("Live stream could not be shown: {Error} {Message}", args.Error, args.ErrorMessage);
            failed();
        };
        Player.Source = MediaSource.CreateFromMediaStreamSource(source);
        Player.Play();
    }

    /// <summary>
    /// Gets the player to attach to a MediaPlayerElement.
    /// </summary>
    public MediaPlayer Player { get; }

    /// <summary>
    /// Gets the size the player was started with.
    /// </summary>
    public (uint Width, uint Height) Size { get; }

    // Where the picture is now on the player's clock.
    private TimeSpan Position()
    {
        try
        {
            return Player.PlaybackSession.Position;
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return TimeSpan.Zero;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The pending sample request ends here, so a new player can read the same packets.
        _feeding.Cancel();
        Player.Dispose();
        _feeding.Dispose();
    }

    private sealed class SampleFeeder(ChannelReader<StreamPacket> packets, CancellationToken cancellationToken, ILogger logger, Func<TimeSpan> position)
    {
        // Frames still waiting here beyond this many are behind live: they are given times that are already due, so
        // the player decodes them (later frames need them) and shows only the newest instead of playing them out.
        private const int Backlog = 2;

        private static readonly TimeSpan MinLead = TimeSpan.FromMilliseconds(30);

        private readonly Func<TimeSpan> _position = position;
        private readonly ChannelReader<StreamPacket> _packets = packets;
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private readonly ILogger _logger = logger;

        // Over this much lead for a whole second, the picture trails live by more than Wi‑Fi needs.
        private static readonly TimeSpan Spare = TimeSpan.FromMilliseconds(60);

        // Trimmed off the lead per frame while there is spare: 1 ms a frame is 3 % faster, which nobody sees.
        private static readonly TimeSpan Trim = TimeSpan.FromMilliseconds(1);

        private readonly System.Diagnostics.Stopwatch _arrivals = System.Diagnostics.Stopwatch.StartNew();
        private TimeSpan _lastArrival;
        private TimeSpan _maxGap;

        // Frame time = phone time since the first frame + this offset, so frames keep the phone's even spacing even
        // when Wi‑Fi delivers them in pairs or bursts. The offset only grows when a frame would be late (a visible
        // hitch, rare) and shrinks slowly while every frame of the last second had lead to spare.
        private TimeSpan _offset;
        private TimeSpan _leastEarly = TimeSpan.MaxValue;
        private TimeSpan _spareSince;
        private TimeSpan _last = TimeSpan.FromTicks(-1);
        private TimeSpan _early;
        private int _skipped;
        private readonly System.Diagnostics.Stopwatch _window = System.Diagnostics.Stopwatch.StartNew();
        private int _framesInWindow;
        private byte[] _config = [];
        private ulong? _firstPts;

        public async Task<MediaStreamSample?> NextAsync()
        {
            while (await _packets.WaitToReadAsync(_cancellationToken))
            {
                if (!_packets.TryRead(out var packet) || packet.Track != StreamTrack.Video)
                {
                    continue;
                }

                if (packet.CodecConfig)
                {
                    _config = packet.Data.ToByteArray();
                    continue;
                }

                if (_firstPts is null && !packet.KeyFrame)
                {
                    continue;
                }

                _firstPts ??= packet.PtsUs;
                Measure();
                var data = packet.KeyFrame ? [.. _config, .. packet.Data.Span] : packet.Data.ToByteArray();
                var phoneTime = TimeSpan.FromTicks((long)(packet.PtsUs - Math.Min(packet.PtsUs, _firstPts.Value)) * 10);
                var now = _arrivals.Elapsed;
                var gap = now - _lastArrival;
                _lastArrival = now;
                _maxGap = gap > _maxGap ? gap : _maxGap;
                var position = _position();
                var time = phoneTime + _offset;

                if (_packets.Count > Backlog)
                {
                    time = position;
                    _skipped++;
                }
                else if (time < position + MinLead)
                {
                    // Late: from here on frames get this much more lead, and the spare is measured afresh.
                    time = position + MinLead;
                    _leastEarly = TimeSpan.MaxValue;
                    _spareSince = now;
                }
                else
                {
                    TrimLead(time - position, now);
                }

                // Times only go forward.
                time = time > _last ? time : _last + TimeSpan.FromMilliseconds(1);
                _offset = time - phoneTime;
                _last = time;
                _early = time - position;
                var sample = MediaStreamSample.CreateFromBuffer(data.AsBuffer(), time);
                sample.KeyFrame = packet.KeyFrame;
                return sample;
            }

            return null;
        }

        private void TrimLead(TimeSpan early, TimeSpan now)
        {
            _leastEarly = early < _leastEarly ? early : _leastEarly;

            if (now - _spareSince < TimeSpan.FromSeconds(1))
            {
                return;
            }

            // A second in which no frame came closer than this: take the spare off, a millisecond a frame.
            if (_leastEarly > MinLead + Spare)
            {
                _offset -= Trim;
                _leastEarly -= Trim;
                return;
            }

            _leastEarly = TimeSpan.MaxValue;
            _spareSince = now;
        }

        // Every few seconds: how many frames the player took and how many wait behind it.
        private void Measure()
        {
            _framesInWindow++;

            if (_window.Elapsed >= TimeSpan.FromSeconds(3))
            {
                _logger.LogInformation("Live stream: {Fps:F1} frames/s handed over, {Queued} packets waiting, {Skipped} caught up, last frame {Early} ms early, longest gap {Gap} ms", _framesInWindow / _window.Elapsed.TotalSeconds, _packets.Count, _skipped, (int)_early.TotalMilliseconds, (int)_maxGap.TotalMilliseconds);
                _skipped = 0;
                _maxGap = TimeSpan.Zero;
                _framesInWindow = 0;
                _window.Restart();
            }
        }
    }
}
