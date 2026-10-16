using System.Buffers.Binary;
using Fenpo.Protocol.V1;
using Google.Protobuf;

namespace Fenpo.Core.Channel;

/// <summary>
/// Envelopes on the TLS control connection: <c>[uint32 big-endian length][Envelope bytes]</c>.
/// </summary>
public static class EnvelopeFraming
{
    /// <summary>
    /// Largest Envelope accepted (control messages are small; file bodies use their own connections).
    /// </summary>
    public const int MaxLength = 1024 * 1024;

    /// <summary>
    /// Writes one Envelope.
    /// </summary>
    /// <param name="stream">Stream.</param>
    /// <param name="envelope">Message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when written and flushed.</returns>
    public static async Task WriteAsync(Stream stream, Envelope envelope, CancellationToken cancellationToken = default)
    {
        var body = envelope.ToByteArray();
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)body.Length);
        body.CopyTo(frame, 4);
        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads one Envelope.
    /// </summary>
    /// <param name="stream">Stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message, or null when the peer closed the connection between messages.</returns>
    /// <exception cref="InvalidDataException">The frame is too long or not an Envelope.</exception>
    public static async Task<Envelope?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (await ReadFrameAsync(stream, cancellationToken) is not { } body)
        {
            return null;
        }

        try
        {
            return Envelope.Parser.ParseFrom(body);
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new InvalidDataException("Frame is not an Envelope", exception);
        }
    }

    /// <summary>
    /// Reads one frame's bytes without parsing them (a data connection starts with a bare TransferStreamHeader).
    /// </summary>
    /// <param name="stream">Stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The frame body, or null when the peer closed the connection between frames.</returns>
    /// <exception cref="InvalidDataException">The frame is too long.</exception>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var prefix = new byte[4];
        var read = 0;

        while (read < 4)
        {
            var count = await stream.ReadAsync(prefix.AsMemory(read), cancellationToken);

            if (count == 0)
            {
                return read == 0 ? null : throw new EndOfStreamException("Connection closed inside a frame header");
            }

            read += count;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix);

        if (length > MaxLength)
        {
            throw new InvalidDataException($"Frame of {length} bytes is over the {MaxLength} byte limit");
        }

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return body;
    }
}
