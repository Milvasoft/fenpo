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
        var feeder = new SampleFeeder(packets, _feeding.Token);
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

    /// <inheritdoc/>
    public void Dispose()
    {
        // The pending sample request ends here, so a new player can read the same packets.
        _feeding.Cancel();
        Player.Dispose();
        _feeding.Dispose();
    }

    private sealed class SampleFeeder(ChannelReader<StreamPacket> packets, CancellationToken cancellationToken)
    {
        private readonly ChannelReader<StreamPacket> _packets = packets;
        private readonly CancellationToken _cancellationToken = cancellationToken;
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
                var data = packet.KeyFrame ? [.. _config, .. packet.Data.Span] : packet.Data.ToByteArray();
                var time = TimeSpan.FromTicks((long)(packet.PtsUs - Math.Min(packet.PtsUs, _firstPts.Value)) * 10);
                var sample = MediaStreamSample.CreateFromBuffer(data.AsBuffer(), time);
                sample.KeyFrame = packet.KeyFrame;
                return sample;
            }

            return null;
        }
    }
}
