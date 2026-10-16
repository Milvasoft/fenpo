namespace Fenpo.Core.Channel;

/// <summary>
/// Splits sealed messages into ATT-sized chunks: <c>[flags: bit0 first, bit1 last][2 byte sequence, big-endian][payload]</c>,
/// sequence from 0 in every message (protocol/README.md, "Chunks").
/// </summary>
public static class BleChunks
{
    /// <summary>Chunk header length.</summary>
    public const int HeaderLength = 3;

    /// <summary>Flag: first chunk of a message.</summary>
    public const byte First = 0x01;

    /// <summary>Flag: last chunk of a message.</summary>
    public const byte Last = 0x02;

    /// <summary>
    /// Gets the largest chunk payload for an ATT MTU (ATT header 3 bytes, chunk header 3 bytes).
    /// </summary>
    /// <param name="attMtu">Negotiated ATT MTU.</param>
    /// <returns>Payload bytes per chunk.</returns>
    public static int PayloadLength(int attMtu) => Math.Max(1, attMtu - 3 - HeaderLength);

    /// <summary>
    /// Splits a sealed message.
    /// </summary>
    /// <param name="message">Sealed message.</param>
    /// <param name="payloadLength">Payload bytes per chunk, from <see cref="PayloadLength"/>.</param>
    /// <returns>Chunks in order.</returns>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> message, int payloadLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(payloadLength, 1);
        var count = Math.Max(1, (message.Length + payloadLength - 1) / payloadLength);
        var chunks = new List<byte[]>(count);

        for (var i = 0; i < count; i++)
        {
            var part = message.Slice(i * payloadLength, Math.Min(payloadLength, message.Length - (i * payloadLength)));
            var chunk = new byte[HeaderLength + part.Length];
            chunk[0] = (byte)((i == 0 ? First : 0) | (i == count - 1 ? Last : 0));
            chunk[1] = (byte)(i >> 8);
            chunk[2] = (byte)i;
            part.CopyTo(chunk.AsSpan(HeaderLength));
            chunks.Add(chunk);
        }

        return chunks;
    }
}

/// <summary>
/// Joins chunks back into sealed messages. A chunk out of sequence or a message over the size limit drops the message
/// being assembled; the next first chunk starts over.
/// </summary>
public sealed class BleChunkAssembler
{
    private readonly List<byte> _buffer = [];
    private int _expected = -1;

    /// <summary>
    /// Gets how many chunks were thrown away so far (out of order, too short, too long, or without a first chunk).
    /// </summary>
    public int Dropped { get; private set; }

    /// <summary>
    /// Adds a chunk.
    /// </summary>
    /// <param name="chunk">Chunk as written or notified.</param>
    /// <param name="message">The complete sealed message, when this was the last chunk.</param>
    /// <returns>True when a message is complete.</returns>
    public bool TryAdd(ReadOnlySpan<byte> chunk, out byte[]? message)
    {
        message = null;

        if (chunk.Length < BleChunks.HeaderLength)
        {
            Dropped++;
            Reset();
            return false;
        }

        var flags = chunk[0];
        var sequence = (chunk[1] << 8) | chunk[2];

        if ((flags & BleChunks.First) != 0)
        {
            _buffer.Clear();
            _expected = 0;
        }

        if (_expected < 0 || sequence != _expected || _buffer.Count + chunk.Length - BleChunks.HeaderLength > BleSeal.MaxSealedLength)
        {
            Dropped++;
            Reset();
            return false;
        }

        _buffer.AddRange(chunk[BleChunks.HeaderLength..]);
        _expected++;

        if ((flags & BleChunks.Last) == 0)
        {
            return false;
        }

        message = [.. _buffer];
        Reset();
        return true;
    }

    private void Reset()
    {
        _buffer.Clear();
        _expected = -1;
    }
}
