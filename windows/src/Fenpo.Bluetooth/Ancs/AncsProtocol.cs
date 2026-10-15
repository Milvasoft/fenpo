using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Fenpo.Core.Notifications;

namespace Fenpo.Bluetooth.Ancs;

/// <summary>
/// ANCS Notification Source event ids.
/// </summary>
public enum AncsEventId : byte
{
    /// <summary>A notification was added.</summary>
    Added = 0,

    /// <summary>A notification was modified.</summary>
    Modified = 1,

    /// <summary>A notification was removed.</summary>
    Removed = 2,
}

/// <summary>
/// ANCS notification attribute ids (Get Notification Attributes).
/// </summary>
public enum AncsAttribute : byte
{
    /// <summary>Bundle id of the app.</summary>
    AppIdentifier = 0,

    /// <summary>Title; needs a maximum length.</summary>
    Title = 1,

    /// <summary>Subtitle; needs a maximum length.</summary>
    Subtitle = 2,

    /// <summary>Message; needs a maximum length.</summary>
    Message = 3,

    /// <summary>Full message length as a decimal string.</summary>
    MessageSize = 4,

    /// <summary>Date as yyyyMMdd'T'HHmmSS.</summary>
    Date = 5,

    /// <summary>Positive action label.</summary>
    PositiveActionLabel = 6,

    /// <summary>Negative action label.</summary>
    NegativeActionLabel = 7,
}

/// <summary>
/// One Notification Source event (8 bytes).
/// </summary>
/// <param name="EventId">Added, modified or removed.</param>
/// <param name="EventFlags">Raw flags: 1 silent, 2 important, 4 pre-existing, 8 positive action, 16 negative action.</param>
/// <param name="CategoryId">ANCS category.</param>
/// <param name="CategoryCount">Number of active notifications in the category.</param>
/// <param name="Uid">Notification uid, valid for this connection only.</param>
public readonly record struct AncsNotificationEvent(AncsEventId EventId, byte EventFlags, byte CategoryId, byte CategoryCount, uint Uid)
{
    /// <summary>
    /// Gets the flags Fenpo cares about.
    /// </summary>
    public NotificationFlags Flags => (NotificationFlags)(EventFlags & 0x07);

    /// <summary>
    /// Gets a value indicating whether the notification has a positive action.
    /// </summary>
    public bool HasPositiveAction => (EventFlags & 0x08) != 0;

    /// <summary>
    /// Gets a value indicating whether the notification has a negative action.
    /// </summary>
    public bool HasNegativeAction => (EventFlags & 0x10) != 0;

    /// <summary>
    /// Gets the category, Other for values this version does not know.
    /// </summary>
    public NotificationCategory Category => CategoryId <= (byte)NotificationCategory.Entertainment ? (NotificationCategory)CategoryId : NotificationCategory.Other;

    /// <summary>
    /// Parses a Notification Source value.
    /// </summary>
    /// <param name="data">Characteristic value.</param>
    /// <param name="value">The event.</param>
    /// <returns>False when the value is too short.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out AncsNotificationEvent value)
    {
        if (data.Length < 8)
        {
            value = default;
            return false;
        }

        value = new AncsNotificationEvent((AncsEventId)data[0], data[1], data[2], data[3], BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
        return true;
    }
}

/// <summary>
/// Builds ANCS Control Point commands.
/// </summary>
public static class AncsCommands
{
    /// <summary>
    /// Command id of Get Notification Attributes.
    /// </summary>
    public const byte GetNotificationAttributesId = 0;

    /// <summary>
    /// Command id of Get App Attributes.
    /// </summary>
    public const byte GetAppAttributesId = 1;

    /// <summary>
    /// Command id of Perform Notification Action.
    /// </summary>
    public const byte PerformNotificationActionId = 2;

    /// <summary>
    /// The attributes Fenpo asks for, in this order.
    /// </summary>
    public static IReadOnlyList<AncsAttribute> NotificationAttributes { get; } =
    [
        AncsAttribute.AppIdentifier,
        AncsAttribute.Title,
        AncsAttribute.Subtitle,
        AncsAttribute.Message,
        AncsAttribute.MessageSize,
        AncsAttribute.Date,
        AncsAttribute.PositiveActionLabel,
        AncsAttribute.NegativeActionLabel,
    ];

    /// <summary>
    /// Builds Get Notification Attributes for <see cref="NotificationAttributes"/>.
    /// </summary>
    /// <param name="uid">Notification uid.</param>
    /// <param name="titleLength">Maximum title and subtitle length in bytes.</param>
    /// <param name="messageLength">Maximum message length in bytes.</param>
    /// <returns>The command bytes.</returns>
    public static byte[] GetNotificationAttributes(uint uid, ushort titleLength = 128, ushort messageLength = 512)
    {
        var command = new List<byte> { GetNotificationAttributesId };
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, uid);
        command.AddRange(buffer.ToArray());

        foreach (var attribute in NotificationAttributes)
        {
            command.Add((byte)attribute);

            if (attribute is AncsAttribute.Title or AncsAttribute.Subtitle or AncsAttribute.Message)
            {
                var length = attribute == AncsAttribute.Message ? messageLength : titleLength;
                command.Add((byte)length);
                command.Add((byte)(length >> 8));
            }
        }

        return [.. command];
    }

    /// <summary>
    /// Builds Get App Attributes asking for the display name.
    /// </summary>
    /// <param name="appIdentifier">Bundle id.</param>
    /// <returns>The command bytes.</returns>
    public static byte[] GetAppDisplayName(string appIdentifier) => [GetAppAttributesId, .. Encoding.UTF8.GetBytes(appIdentifier), 0, 0];

    /// <summary>
    /// Builds Perform Notification Action.
    /// </summary>
    /// <param name="uid">Notification uid.</param>
    /// <param name="positive">True for the positive action.</param>
    /// <returns>The command bytes.</returns>
    public static byte[] PerformNotificationAction(uint uid, bool positive)
    {
        var command = new byte[6];
        command[0] = PerformNotificationActionId;
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(1), uid);
        command[5] = positive ? (byte)0 : (byte)1;
        return command;
    }

    /// <summary>
    /// Parses the ANCS date attribute (phone local time, yyyyMMdd'T'HHmmSS).
    /// </summary>
    /// <param name="value">Attribute text.</param>
    /// <param name="offset">UTC offset to assume (the PC's).</param>
    /// <returns>The date, or null when malformed.</returns>
    public static DateTimeOffset? ParseDate(string value, TimeSpan offset) =>
        DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(local, offset)
            : null;
}
