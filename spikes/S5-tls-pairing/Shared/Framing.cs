using System.Buffers.Binary;
using Fenpo.Protocol.V1;
using Google.Protobuf;

namespace Fenpo.Spikes.S5;

/// <summary>
/// Control channel framing: [uint32 big-endian length][Envelope bytes].
/// </summary>
public sealed class EnvelopeChannel(Stream stream)
{
    public const int MaxEnvelopeLength = 1024 * 1024;

    private readonly Stream _stream = stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private ulong _nextId = 1;

    public async Task<ulong> SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        envelope.Version = 1;
        envelope.Id = Interlocked.Increment(ref _nextId) - 1;
        var body = envelope.ToByteArray();
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)body.Length);
        body.CopyTo(frame, 4);

        await _writeGate.WaitAsync(cancellationToken);

        try
        {
            await _stream.WriteAsync(frame, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }

        return envelope.Id;
    }

    public async Task<Envelope?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var prefix = new byte[4];

        try
        {
            await _stream.ReadExactlyAsync(prefix, cancellationToken);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix);

        if (length > MaxEnvelopeLength)
        {
            throw new InvalidDataException($"Envelope of {length} bytes exceeds the {MaxEnvelopeLength} byte limit");
        }

        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, cancellationToken);
        return Envelope.Parser.ParseFrom(body);
    }
}
