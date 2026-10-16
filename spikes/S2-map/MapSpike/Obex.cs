using System.Buffers.Binary;
using System.Text;

namespace Fenpo.Spikes.S2;

/// <summary>
/// OBEX header ids used by MAP.
/// </summary>
internal static class ObexHeaderId
{
    public const byte Name = 0x01;
    public const byte Type = 0x42;
    public const byte Length = 0xC3;
    public const byte Target = 0x46;
    public const byte Body = 0x48;
    public const byte EndOfBody = 0x49;
    public const byte Who = 0x4A;
    public const byte ConnectionId = 0xCB;
    public const byte ApplicationParameters = 0x4C;
}

/// <summary>
/// OBEX opcodes and response codes (final bit included).
/// </summary>
internal static class ObexCode
{
    public const byte Connect = 0x80;
    public const byte Disconnect = 0x81;
    public const byte Put = 0x02;
    public const byte PutFinal = 0x82;
    public const byte GetFinal = 0x83;
    public const byte SetPath = 0x85;

    public const byte Continue = 0x90;
    public const byte Success = 0xA0;

    public static string Name(byte code) => code switch
    {
        0x80 => "Connect",
        0x81 => "Disconnect",
        0x02 => "Put",
        0x82 => "PutFinal",
        0x03 or 0x83 => "Get",
        0x85 => "SetPath",
        0x90 => "Continue",
        0xA0 => "Success",
        0xC0 => "BadRequest",
        0xC1 => "Unauthorized",
        0xC3 => "Forbidden",
        0xC4 => "NotFound",
        0xC5 => "MethodNotAllowed",
        0xC6 => "NotAcceptable",
        0xCC => "PreconditionFailed",
        0xCF => "UnsupportedMediaType",
        0xD0 => "InternalServerError",
        0xD1 => "NotImplemented",
        0xD3 => "ServiceUnavailable",
        _ => $"0x{code:X2}",
    };
}

/// <summary>
/// One OBEX header. Value holds the encoded payload without the id and length bytes.
/// </summary>
internal sealed record ObexHeader(byte Id, byte[] Value)
{
    public static ObexHeader Name(string name) => new(ObexHeaderId.Name, name.Length == 0 ? [] : [.. Encoding.BigEndianUnicode.GetBytes(name), 0, 0]);

    public static ObexHeader Type(string type) => new(ObexHeaderId.Type, [.. Encoding.ASCII.GetBytes(type), 0]);

    public static ObexHeader ConnectionId(uint id) => new(ObexHeaderId.ConnectionId, BigEndian(id));

    public static ObexHeader Target(byte[] uuid) => new(ObexHeaderId.Target, uuid);

    public static ObexHeader Who(byte[] uuid) => new(ObexHeaderId.Who, uuid);

    public static ObexHeader AppParameters(byte[] value) => new(ObexHeaderId.ApplicationParameters, value);

    public static ObexHeader Body(byte[] value, bool end) => new(end ? ObexHeaderId.EndOfBody : ObexHeaderId.Body, value);

    public string AsText() => (Id & 0xC0) switch
    {
        0x00 => Encoding.BigEndianUnicode.GetString(Value).TrimEnd('\0'),
        0x40 => Encoding.UTF8.GetString(Value).TrimEnd('\0'),
        0xC0 => BinaryPrimitives.ReadUInt32BigEndian(Value).ToString(),
        _ => Value.Length > 0 ? Value[0].ToString() : string.Empty,
    };

    public int EncodedLength => (Id & 0xC0) switch
    {
        0x00 or 0x40 => 3 + Value.Length,
        0x80 => 2,
        _ => 5,
    };

    public void WriteTo(List<byte> output)
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

    private static byte[] BigEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}

/// <summary>
/// An OBEX request or response: code, optional fixed fields (Connect, SetPath), headers.
/// </summary>
internal sealed class ObexPacket(byte code, byte[] fields, List<ObexHeader> headers)
{
    public byte Code { get; } = code;

    public byte[] Fields { get; } = fields;

    public List<ObexHeader> Headers { get; } = headers;

    public ObexHeader? Header(byte id) => Headers.FirstOrDefault(h => h.Id == id);

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

    public static ObexPacket Decode(byte[] data, int fieldLength)
    {
        var headers = new List<ObexHeader>();
        var position = 3 + fieldLength;

        while (position < data.Length)
        {
            var id = data[position];

            switch (id & 0xC0)
            {
                case 0x00:
                case 0x40:
                    var length = (data[position + 1] << 8) | data[position + 2];
                    headers.Add(new ObexHeader(id, data[(position + 3)..(position + length)]));
                    position += length;
                    break;
                case 0x80:
                    headers.Add(new ObexHeader(id, [data[position + 1]]));
                    position += 2;
                    break;
                default:
                    headers.Add(new ObexHeader(id, data[(position + 1)..(position + 5)]));
                    position += 5;
                    break;
            }
        }

        return new ObexPacket(data[0], data[3..(3 + fieldLength)], headers);
    }

    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var prefix = new byte[3];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        var length = (prefix[1] << 8) | prefix[2];
        var packet = new byte[length];
        prefix.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(packet.AsMemory(3, length - 3), cancellationToken);
        return packet;
    }
}

/// <summary>
/// MAP application parameter tags and a small TLV builder and reader (values are big-endian).
/// </summary>
internal static class AppParams
{
    public const byte MaxListCount = 0x01;
    public const byte ListStartOffset = 0x02;
    public const byte Attachment = 0x0A;
    public const byte Transparent = 0x0B;
    public const byte Retry = 0x0C;
    public const byte NewMessage = 0x0D;
    public const byte NotificationStatus = 0x0E;
    public const byte MasInstanceId = 0x0F;
    public const byte FolderListingSize = 0x11;
    public const byte ListingSize = 0x12;
    public const byte SubjectLength = 0x13;
    public const byte Charset = 0x14;
    public const byte StatusIndicator = 0x17;
    public const byte StatusValue = 0x18;
    public const byte MseTime = 0x19;
    public const byte MapSupportedFeatures = 0x29;

    public static byte[] Build(params (byte Tag, byte[] Value)[] values)
    {
        var output = new List<byte>();

        foreach (var (tag, value) in values)
        {
            output.Add(tag);
            output.Add((byte)value.Length);
            output.AddRange(value);
        }

        return [.. output];
    }

    public static byte[] U8(byte value) => [value];

    public static byte[] U16(ushort value) => [(byte)(value >> 8), (byte)value];

    public static byte[] U32(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    public static string Describe(byte[] data)
    {
        var parts = new List<string>();
        var position = 0;

        while (position + 2 <= data.Length)
        {
            var tag = data[position];
            var length = data[position + 1];
            var value = data[(position + 2)..Math.Min(data.Length, position + 2 + length)];
            var text = tag == MseTime ? Encoding.ASCII.GetString(value) : Convert.ToHexString(value);
            parts.Add($"0x{tag:X2}={text}");
            position += 2 + length;
        }

        return string.Join(" ", parts);
    }
}
