namespace Fenpo.Bluetooth.Map;

/// <summary>
/// MAP application parameters (MAP 1.4, section 6.3.1): tag, length, big-endian value.
/// </summary>
public static class MapParameters
{
    /// <summary>MaxListCount (2 bytes).</summary>
    public const byte MaxListCount = 0x01;

    /// <summary>ListStartOffset (2 bytes).</summary>
    public const byte ListStartOffset = 0x02;

    /// <summary>Attachment (1 byte).</summary>
    public const byte Attachment = 0x0A;

    /// <summary>Transparent (1 byte).</summary>
    public const byte Transparent = 0x0B;

    /// <summary>Retry (1 byte).</summary>
    public const byte Retry = 0x0C;

    /// <summary>NotificationStatus (1 byte).</summary>
    public const byte NotificationStatus = 0x0E;

    /// <summary>ListingSize (2 bytes).</summary>
    public const byte ListingSize = 0x12;

    /// <summary>SubjectLength (1 byte).</summary>
    public const byte SubjectLength = 0x13;

    /// <summary>Charset (1 byte): 1 is UTF-8.</summary>
    public const byte Charset = 0x14;

    /// <summary>StatusIndicator (1 byte): 0 is read status.</summary>
    public const byte StatusIndicator = 0x17;

    /// <summary>StatusValue (1 byte).</summary>
    public const byte StatusValue = 0x18;

    /// <summary>MSETime (text).</summary>
    public const byte MseTime = 0x19;

    /// <summary>
    /// Encodes parameters.
    /// </summary>
    /// <param name="values">Tag and value pairs.</param>
    /// <returns>The TLV bytes.</returns>
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

    /// <summary>
    /// Encodes a one-byte value.
    /// </summary>
    /// <param name="value">Value.</param>
    /// <returns>The bytes.</returns>
    public static byte[] U8(byte value) => [value];

    /// <summary>
    /// Encodes a two-byte value.
    /// </summary>
    /// <param name="value">Value.</param>
    /// <returns>The bytes.</returns>
    public static byte[] U16(ushort value) => [(byte)(value >> 8), (byte)value];

    /// <summary>
    /// Reads a parameter from TLV bytes.
    /// </summary>
    /// <param name="data">TLV bytes.</param>
    /// <param name="tag">Tag.</param>
    /// <returns>The value, or null when absent.</returns>
    public static byte[]? Find(byte[]? data, byte tag)
    {
        var position = 0;

        while (data is not null && position + 2 <= data.Length)
        {
            var length = data[position + 1];

            if (position + 2 + length > data.Length)
            {
                return null;
            }

            if (data[position] == tag)
            {
                return data[(position + 2)..(position + 2 + length)];
            }

            position += 2 + length;
        }

        return null;
    }
}
