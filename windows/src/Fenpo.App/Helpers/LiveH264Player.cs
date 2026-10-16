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
        var feeder = new SampleFeeder(packets, _feeding.Token, logger, CatchUp);
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

    // A live picture must not trail what arrived by more than the lead it needs: when it does, the player plays a bit
    // faster until it is back. Returns how far the newest frame is ahead of the picture.
    private TimeSpan CatchUp(TimeSpan newest, TimeSpan lead)
    {
        try
        {
            var session = Player.PlaybackSession;
            var behind = newest - session.Position;
            // 5 % faster is not visible and trims a lead that grew past what the gaps need; far behind catches up fast.
            var rate = behind > lead + TimeSpan.FromSeconds(1) ? 2.0 : behind > lead + TimeSpan.FromMilliseconds(100) ? 1.05 : 1.0;

            if (Math.Abs(session.PlaybackRate - rate) > 0.01)
            {
                session.PlaybackRate = rate;
            }

            return behind;
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

    private sealed class SampleFeeder(ChannelReader<StreamPacket> packets, CancellationToken cancellationToken, ILogger logger, Func<TimeSpan, TimeSpan, TimeSpan> catchUp)
    {
        private readonly Func<TimeSpan, TimeSpan, TimeSpan> _catchUp = catchUp;
        private TimeSpan _behind;

        // How far the frame times are moved forward: the player's clock starts before the first frame arrives, so
        // frames would reach it after their presentation time and play jerky or be dropped.
        private TimeSpan _shift;

        // Frames come over Wi‑Fi in bursts; the player keeps a lead of a little more than the longest recent gap
        // between them, so a gap does not leave it without a frame.
        private readonly System.Diagnostics.Stopwatch _arrivals = System.Diagnostics.Stopwatch.StartNew();
        private TimeSpan _lastArrival;
        private TimeSpan _maxGap;
        private TimeSpan _recentGap = TimeSpan.FromMilliseconds(60);
        private readonly ChannelReader<StreamPacket> _packets = packets;
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private readonly ILogger _logger = logger;
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
                var time = TimeSpan.FromTicks((long)(packet.PtsUs - Math.Min(packet.PtsUs, _firstPts.Value)) * 10) + _shift;
                var now = _arrivals.Elapsed;
                var gap = now - _lastArrival;
                _lastArrival = now;
                _maxGap = gap > _maxGap ? gap : _maxGap;

                // The longest gap lately, slowly forgotten (about 1 % per frame) so a single burst does not stick.
                _recentGap = gap > _recentGap ? gap : _recentGap - ((_recentGap - TimeSpan.FromMilliseconds(60)) / 100);
                var lead = TimeSpan.FromMilliseconds(Math.Clamp(_recentGap.TotalMilliseconds + 40, 80, 600));
                _behind = _catchUp(time, lead);

                // Frames arriving too close to (or after) their time: move the timeline so they have the lead.
                if (_behind < lead)
                {
                    var move = lead - _behind;
                    _shift += move;
                    time += move;
                }
                var sample = MediaStreamSample.CreateFromBuffer(data.AsBuffer(), time);
                sample.KeyFrame = packet.KeyFrame;
                return sample;
            }

            return null;
        }

        // Every few seconds: how many frames the player took and how many wait behind it.
        private void Measure()
        {
            _framesInWindow++;

            if (_window.Elapsed >= TimeSpan.FromSeconds(3))
            {
                _logger.LogInformation("Live stream: {Fps:F1} frames/s shown, {Queued} packets waiting, frames {Lead} ms early, longest gap {Gap} ms, timeline moved {Shift} ms", _framesInWindow / _window.Elapsed.TotalSeconds, _packets.Count, (int)_behind.TotalMilliseconds, (int)_maxGap.TotalMilliseconds, (int)_shift.TotalMilliseconds);
                _maxGap = TimeSpan.Zero;
                _framesInWindow = 0;
                _window.Restart();
            }
        }
    }
}
