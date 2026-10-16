using System.Globalization;
using System.Text;
using Fenpo.Core.Media;

namespace Fenpo.Bluetooth.Ams;

/// <summary>
/// AMS entities.
/// </summary>
public enum AmsEntity : byte
{
    /// <summary>The player app.</summary>
    Player = 0,

    /// <summary>The play queue.</summary>
    Queue = 1,

    /// <summary>The current track.</summary>
    Track = 2,
}

/// <summary>
/// One Entity Update notification: [entity][attribute][flags][UTF-8 value].
/// </summary>
/// <param name="Entity">Entity.</param>
/// <param name="Attribute">Attribute id within the entity.</param>
/// <param name="Truncated">True when the value was cut at the MTU and must be read from Entity Attribute.</param>
/// <param name="Value">Value text.</param>
public readonly record struct AmsEntityUpdate(AmsEntity Entity, byte Attribute, bool Truncated, string Value)
{
    /// <summary>
    /// Parses an Entity Update value.
    /// </summary>
    /// <param name="data">Characteristic value.</param>
    /// <param name="update">The update.</param>
    /// <returns>False when the value is too short.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out AmsEntityUpdate update)
    {
        if (data.Length < 3)
        {
            update = default;
            return false;
        }

        update = new AmsEntityUpdate((AmsEntity)data[0], data[1], (data[2] & 0x01) != 0, Encoding.UTF8.GetString(data[3..]));
        return true;
    }
}

/// <summary>
/// AMS commands and the reducer that turns updates into <see cref="NowPlaying"/>.
/// </summary>
public static class AmsProtocol
{
    /// <summary>
    /// Entity Update registrations, one write each: player (name, playback info, volume), queue (index, count, shuffle,
    /// repeat), track (artist, album, title, duration).
    /// </summary>
    public static IReadOnlyList<byte[]> Registrations { get; } =
    [
        [(byte)AmsEntity.Player, 0, 1, 2],
        [(byte)AmsEntity.Queue, 0, 1, 2, 3],
        [(byte)AmsEntity.Track, 0, 1, 2, 3],
    ];

    /// <summary>
    /// Parses the Remote Command notification (one byte per supported command).
    /// </summary>
    /// <param name="data">Characteristic value.</param>
    /// <returns>Supported commands.</returns>
    public static IReadOnlySet<MediaCommand> ParseSupportedCommands(ReadOnlySpan<byte> data)
    {
        var commands = new HashSet<MediaCommand>();

        foreach (var value in data)
        {
            if (value <= (byte)MediaCommand.BookmarkTrack)
            {
                commands.Add((MediaCommand)value);
            }
        }

        return commands;
    }

    /// <summary>
    /// Applies an update to the current state.
    /// </summary>
    /// <param name="current">State so far.</param>
    /// <param name="update">Entity update (with the full value when it was truncated).</param>
    /// <param name="at">When it arrived; anchors the elapsed time.</param>
    /// <returns>The new state.</returns>
    public static NowPlaying Apply(NowPlaying current, AmsEntityUpdate update, DateTimeOffset at) => (update.Entity, update.Attribute) switch
    {
        (AmsEntity.Player, 0) => current with { PlayerName = update.Value },
        (AmsEntity.Player, 1) => ApplyPlaybackInfo(current, update.Value, at),
        (AmsEntity.Player, 2) => current with { Volume = ParseDouble(update.Value) },
        (AmsEntity.Track, 0) => current with { Artist = update.Value },
        (AmsEntity.Track, 1) => current with { Album = update.Value },
        (AmsEntity.Track, 2) => current with { Title = update.Value },
        (AmsEntity.Track, 3) => current with { Duration = ParseDouble(update.Value) is { } seconds ? TimeSpan.FromSeconds(seconds) : null },
        _ => current,
    };

    // "state,rate,elapsed", e.g. "1,1.0,38.392"; parts may be empty ("0,,") when nothing plays.
    private static NowPlaying ApplyPlaybackInfo(NowPlaying current, string value, DateTimeOffset at)
    {
        var parts = value.Split(',');
        var state = parts.Length > 0 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s is >= 0 and <= 3 ? (PlaybackState)s : PlaybackState.Paused;
        var rate = parts.Length > 1 ? ParseDouble(parts[1]) ?? 0 : 0;
        var elapsed = parts.Length > 2 ? ParseDouble(parts[2]) ?? 0 : 0;
        return current with { State = state, Rate = rate, Elapsed = TimeSpan.FromSeconds(elapsed), ElapsedAt = at };
    }

    private static double? ParseDouble(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
}
