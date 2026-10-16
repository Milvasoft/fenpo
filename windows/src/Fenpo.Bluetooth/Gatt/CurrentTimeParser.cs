using System.Buffers.Binary;

namespace Fenpo.Bluetooth.Gatt;

/// <summary>
/// Parses the Current Time characteristic (Exact Time 256 + adjust reason).
/// </summary>
public static class CurrentTimeParser
{
    /// <summary>
    /// Parses year (LE16), month, day, hours, minutes, seconds; the phone's local wall clock time.
    /// </summary>
    /// <param name="data">Characteristic value.</param>
    /// <param name="value">The time.</param>
    /// <returns>False when the value is too short or not a valid date.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out DateTime value)
    {
        value = default;

        if (data.Length < 7)
        {
            return false;
        }

        try
        {
            value = new DateTime(BinaryPrimitives.ReadUInt16LittleEndian(data), data[2], data[3], data[4], data[5], data[6], DateTimeKind.Local);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
