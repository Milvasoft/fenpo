using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Logging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;

namespace Fenpo.App.Helpers;

/// <summary>
/// Plays the phone's sound from a live stream (video.proto, PCM 16-bit little-endian) with a small, steady lead over the
/// player's clock, like <see cref="LiveH264Player"/> does for the picture. Used by the screen mirror window.
/// </summary>
public sealed class LivePcmPlayer : IDisposable
{
    private readonly CancellationTokenSource _feeding = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LivePcmPlayer"/> class and starts playing.
    /// </summary>
    /// <param name="packets">The stream's audio packets.</param>
    /// <param name="sampleRate">Samples per second.</param>
    /// <param name="channels">Channel count.</param>
    /// <param name="logger">Logger.</param>
    public LivePcmPlayer(ChannelReader<StreamPacket> packets, uint sampleRate, uint channels, ILogger logger)
    {
        sampleRate = sampleRate == 0 ? 48000 : sampleRate;
        channels = channels == 0 ? 2 : channels;
        var properties = AudioEncodingProperties.CreatePcm(sampleRate, channels, 16);
        var source = new MediaStreamSource(new AudioStreamDescriptor(properties)) { BufferTime = TimeSpan.Zero, IsLive = true, CanSeek = false };
        var feeder = new SampleFeeder(packets, sampleRate * channels * 2, _feeding.Token, Position);
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
                logger.LogWarning(exception, "Feeding the live sound failed");
                args.Request.Sample = null;
            }
            finally
            {
                deferral.Complete();
            }
        };

        Player = new MediaPlayer { RealTimePlayback = true, AudioCategory = MediaPlayerAudioCategory.Media };
        Player.MediaFailed += (sender, args) => logger.LogWarning("Live sound could not be played: {Error} {Message}", args.Error, args.ErrorMessage);
        Player.Source = MediaSource.CreateFromMediaStreamSource(source);
        Player.Play();
    }

    /// <summary>
    /// Gets the player.
    /// </summary>
    public MediaPlayer Player { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _feeding.Cancel();
        Player.Dispose();
        _feeding.Dispose();
    }

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

    private sealed class SampleFeeder(ChannelReader<StreamPacket> packets, uint bytesPerSecond, CancellationToken cancellationToken, Func<TimeSpan> position)
    {
        // Sound has no key frames to skip to: a little lead absorbs Wi‑Fi bursts, and when it grows past the most the
        // ear tolerates, the oldest queued sound is dropped instead of playing late.
        private static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(60);
        private static readonly TimeSpan MaxLead = TimeSpan.FromMilliseconds(250);

        private readonly ChannelReader<StreamPacket> _packets = packets;
        private readonly uint _bytesPerSecond = bytesPerSecond;
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private readonly Func<TimeSpan> _position = position;
        private TimeSpan _next = TimeSpan.FromTicks(-1);

        public async Task<MediaStreamSample?> NextAsync()
        {
            while (await _packets.WaitToReadAsync(_cancellationToken))
            {
                if (!_packets.TryRead(out var packet) || packet.Track != StreamTrack.Audio || packet.CodecConfig || packet.Data.IsEmpty)
                {
                    continue;
                }

                var position = _position();

                // Back-to-back samples keep the sound continuous; a gap (or the start) begins again a little ahead.
                if (_next < position + TimeSpan.FromMilliseconds(5))
                {
                    _next = position + Lead;
                }
                else if (_next > position + MaxLead)
                {
                    continue;
                }

                var data = packet.Data.ToByteArray();
                var duration = TimeSpan.FromTicks(data.Length * TimeSpan.TicksPerSecond / _bytesPerSecond);
                var sample = MediaStreamSample.CreateFromBuffer(data.AsBuffer(), _next);
                sample.Duration = duration;
                _next += duration;
                return sample;
            }

            return null;
        }
    }
}
