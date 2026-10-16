using System.Buffers.Binary;
using System.Text;

namespace Fenpo.Bluetooth.Obex;

/// <summary>
/// OBEX opcodes (final bit included where MAP always sets it).
/// </summary>
public static class ObexOpcode
{
    /// <summary>Connect.</summary>
    public const byte Connect = 0x80;

    /// <summary>Disconnect.</summary>
    public const byte Disconnect = 0x81;

    /// <summary>Put, more packets follow.</summary>
    public const byte Put = 0x02;

    /// <summary>Put, last packet.</summary>
    public const byte PutFinal = 0x82;

    /// <summary>Get, final.</summary>
    public const byte GetFinal = 0x83;

    /// <summary>SetPath.</summary>
    public const byte SetPath = 0x85;
}

/// <summary>
/// OBEX response codes (final bit included).
/// </summary>
public static class ObexResponse
{
    /// <summary>Continue.</summary>
    public const byte Continue = 0x90;

    /// <summary>Success.</summary>
    public const byte Success = 0xA0;

    /// <summary>Bad request.</summary>
    public const byte BadRequest = 0xC0;

    /// <summary>Forbidden: on the iPhone, message access is off for this PC.</summary>
    public const byte Forbidden = 0xC3;

    /// <summary>Not found.</summary>
    public const byte NotFound = 0xC4;

    /// <summary>Not acceptable.</summary>
    public const byte NotAcceptable = 0xC6;

    /// <summary>Not implemented.</summary>
    public const byte NotImplemented = 0xD1;

    /// <summary>Service unavailable.</summary>
    public const byte ServiceUnavailable = 0xD3;
}

/// <summary>
/// OBEX header ids used by MAP.
/// </summary>
public static class ObexHeaderId
{
    /// <summary>Name (UTF-16BE, null terminated).</summary>
    public const byte Name = 0x01;

    /// <summary>Type (ASCII, null terminated).</summary>
    public const byte Type = 0x42;

    /// <summary>Target (bytes).</summary>
    public const byte Target = 0x46;

    /// <summary>Body (bytes).</summary>
    public const byte Body = 0x48;

    /// <summary>End of body (bytes).</summary>
    public const byte EndOfBody = 0x49;

    /// <summary>Who (bytes).</summary>
    public const byte Who = 0x4A;

    /// <summary>Application parameters (TLV bytes).</summary>
    public const byte ApplicationParameters = 0x4C;

    /// <summary>Connection id (4 bytes).</summary>
    public const byte ConnectionId = 0xCB;
}

/// <summary>
/// One OBEX header. <see cref="Value"/> is the payload without the id and length bytes.
/// </summary>
/// <param name="Id">Header id; its top two bits give the encoding.</param>
/// <param name="Value">Payload.</param>
public sealed record ObexHeader(byte Id, byte[] Value)
{
    /// <summary>
    /// Gets the number of bytes the header takes on the wire.
    /// </summary>
    public int EncodedLength => (Id & 0xC0) switch
    {
        0x00 or 0x40 => 3 + Value.Length,
        0x80 => 2,
        _ => 5,
    };

    /// <summary>
    /// Creates a Name header; an empty name is sent as an empty header.
    /// </summary>
    /// <param name="name">Name.</param>
    /// <returns>The header.</returns>
    public static ObexHeader Name(string name) => new(ObexHeaderId.Name, name.Length == 0 ? [] : [.. Encoding.BigEndianUnicode.GetBytes(name), 0, 0]);

    /// <summary>
    /// Creates a Type header.
    /// </summary>
    /// <param name="type">MIME type.</param>
    /// <returns>The header.</returns>
    public static ObexHeader Type(string type) => new(ObexHeaderId.Type, [.. Encoding.ASCII.GetBytes(type), 0]);

    /// <summary>
    /// Creates a Connection Id header.
    /// </summary>
    /// <param name="id">Connection id.</param>
    /// <returns>The header.</returns>
    public static ObexHeader ConnectionId(uint id)
    {
        var value = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, id);
        return new(ObexHeaderId.ConnectionId, value);
    }

    /// <summary>
    /// Gets the payload as text (Name: UTF-16BE, byte sequences: UTF-8), without the terminating null.
    /// </summary>
    /// <returns>The text.</returns>
    public string AsText() => (Id & 0xC0) == 0x00
        ? Encoding.BigEndianUnicode.GetString(Value).TrimEnd('\0')
        : Encoding.UTF8.GetString(Value).TrimEnd('\0');

    /// <summary>
    /// Gets a 4-byte payload as a number.
    /// </summary>
    /// <returns>The value.</returns>
    public uint AsUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Value);

    internal void WriteTo(List<byte> output)
    {
        output.Add(Id);

        switch (Id & 0xC0)
        {
            case 0x00:
            case 0x40:
                output.Add((byte)((3 + Value.Length) >> 8));
                output.Add((byte)(3 + Value.Length));
                output.AddRange(Value);
                break;
            case 0x80:
                output.Add(Value[0]);
                break;
            default:
                output.AddRange(Value);
                break;
        }
    }
}

/// <summary>
/// An OBEX request or response: code, fixed fields (Connect: 4 bytes, SetPath request: 2 bytes), headers.
/// </summary>
/// <param name="Code">Opcode or response code.</param>
/// <param name="Fields">Fixed fields after the length.</param>
/// <param name="Headers">Headers in order.</param>
public sealed record ObexPacket(byte Code, byte[] Fields, IReadOnlyList<ObexHeader> Headers)
{
    /// <summary>
    /// Gets the first header with the id, or null.
    /// </summary>
    /// <param name="id">Header id.</param>
    /// <returns>The header.</returns>
    public ObexHeader? Header(byte id) => Headers.FirstOrDefault(h => h.Id == id);

    /// <summary>
    /// Gets the Body and End of Body payloads joined.
    /// </summary>
    /// <returns>The body bytes in this packet.</returns>
    public IEnumerable<byte> BodyBytes() => Headers.Where(h => h.Id is ObexHeaderId.Body or ObexHeaderId.EndOfBody).SelectMany(h => h.Value);

    /// <summary>
    /// Encodes the packet.
    /// </summary>
    /// <returns>The bytes on the wire.</returns>
    public byte[] Encode()
    {
        var output = new List<byte> { Code, 0, 0 };
        output.AddRange(Fields);

        foreach (var header in Headers)
        {
            header.WriteTo(output);
        }

        output[1] = (byte)(output.Count >> 8);
        output[2] = (byte)output.Count;
        return [.. output];
    }

    /// <summary>
    /// Decodes a packet.
    /// </summary>
    /// <param name="data">The whole packet.</param>
    /// <param name="fieldLength">Length of the fixed fields, which depends on the request it belongs to.</param>
    /// <returns>The packet.</returns>
    /// <exception cref="InvalidDataException">A header runs past the end of the packet.</exception>
    public static ObexPacket Decode(ReadOnlySpan<byte> data, int fieldLength)
    {
        var headers = new List<ObexHeader>();
        var position = 3 + fieldLength;

        while (position < data.Length)
        {
            var id = data[position];
            var prefixed = (id & 0xC0) is 0x00 or 0x40;
            var length = prefixed
                ? (position + 3 <= data.Length ? (data[position + 1] << 8) | data[position + 2] : 0)
                : (id & 0xC0) == 0x80 ? 2 : 5;

            if ((prefixed && length < 3) || length > data.Length - position)
            {
                throw new InvalidDataException($"OBEX header 0x{id:X2} at {position} runs past the packet ({data.Length} bytes)");
            }

            var payloadStart = prefixed ? 3 : 1;
            headers.Add(new ObexHeader(id, data[(position + payloadStart)..(position + length)].ToArray()));
            position += length;
        }

        return new ObexPacket(data[0], data[3..(3 + fieldLength)].ToArray(), headers);
    }

    /// <summary>
    /// Reads one packet from a stream.
    /// </summary>
    /// <param name="stream">Stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The packet bytes.</returns>
    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var prefix = new byte[3];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        var length = (prefix[1] << 8) | prefix[2];

        if (length < 3)
        {
            throw new InvalidDataException($"OBEX packet length {length}");
        }

        var packet = new byte[length];
        prefix.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(packet.AsMemory(3, length - 3), cancellationToken);
        return packet;
    }
}
