using Fenpo.Protocol.V1;

namespace Fenpo.Core.Channel;

/// <summary>
/// Takes the phone's media connections: TLS connections to the control server port whose first frame was an Envelope
/// with <c>media_stream_open</c>, followed by <c>[uint32 length][StreamPacket]</c> frames (video.proto).
/// </summary>
public interface IMediaStreamConnections
{
    /// <summary>
    /// Reads one stream until the phone closes it. The connection closes when this returns.
    /// </summary>
    /// <param name="device">Phone whose pinned certificate opened the connection.</param>
    /// <param name="open">What the stream carries.</param>
    /// <param name="stream">The TLS stream, past the first frame.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the stream ended.</returns>
    Task HandleAsync(PairedAppDevice device, MediaStreamOpen open, Stream stream, CancellationToken cancellationToken);
}
