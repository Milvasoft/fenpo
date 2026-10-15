using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fenpo.Sdk;

/// <summary>
/// Names of Fenpo's local API: newline-delimited JSON-RPC 2.0 on \\.\pipe\fenpo, open to the same user only
/// (docs/ARCHITECTURE.md, "Yerel API").
/// </summary>
public static class FenpoApi
{
    /// <summary>Pipe name.</summary>
    public const string PipeName = "fenpo";

    /// <summary>Method: <see cref="PhoneStatusInfo"/>.</summary>
    public const string Status = "fenpo.status";

    /// <summary>Method: <see cref="MediaInfo"/>.</summary>
    public const string MediaState = "fenpo.media.state";

    /// <summary>Method: start playback on the phone.</summary>
    public const string MediaPlay = "fenpo.media.play";

    /// <summary>Method: pause playback on the phone.</summary>
    public const string MediaPause = "fenpo.media.pause";

    /// <summary>Method: next track.</summary>
    public const string MediaNext = "fenpo.media.next";

    /// <summary>Method: newest notifications, parameter { limit }.</summary>
    public const string NotificationsRecent = "fenpo.notifications.recent";

    /// <summary>Method: put text on the phone's clipboard, parameter { text }. Needs Fenpo on the iPhone.</summary>
    public const string ClipboardSendToPhone = "fenpo.clipboard.sendToPhone";

    /// <summary>Method: make the phone ring. Needs Fenpo on the iPhone.</summary>
    public const string Ring = "fenpo.ring";

    /// <summary>Event: <see cref="PhoneStatusInfo"/>.</summary>
    public const string StatusChanged = "status.changed";

    /// <summary>Event: <see cref="NotificationInfo"/>.</summary>
    public const string NotificationAdded = "notification.added";

    /// <summary>Event: <see cref="MediaInfo"/>.</summary>
    public const string MediaChanged = "media.changed";

    /// <summary>Event: <see cref="CallInfo"/>.</summary>
    public const string CallIncoming = "call.incoming";

    /// <summary>
    /// Serializer options shared by server and client: camelCase, no nulls.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>
/// JSON-RPC error codes used by Fenpo.
/// </summary>
public static class FenpoErrorCodes
{
    /// <summary>Unreadable JSON.</summary>
    public const int ParseError = -32700;

    /// <summary>Not a valid request.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>Unknown method.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Bad parameters.</summary>
    public const int InvalidParams = -32602;

    /// <summary>No phone in reach.</summary>
    public const int PhoneUnavailable = -32001;

    /// <summary>Needs Fenpo on the iPhone.</summary>
    public const int NeedsPhoneApp = -32002;
}

/// <summary>
/// Phone status.
/// </summary>
/// <param name="Connected">Whether the phone is connected.</param>
/// <param name="DeviceName">Phone name.</param>
/// <param name="Battery">Battery percent, when known.</param>
/// <param name="Links">Active links: "bluetooth", "wifi", "usb".</param>
public sealed record PhoneStatusInfo(bool Connected, string DeviceName, int? Battery, IReadOnlyList<string> Links);

/// <summary>
/// What the phone is playing.
/// </summary>
/// <param name="HasTrack">Whether there is a track.</param>
/// <param name="Playing">Whether it is playing.</param>
/// <param name="Title">Title.</param>
/// <param name="Artist">Artist.</param>
/// <param name="Album">Album.</param>
/// <param name="PositionSeconds">Position, when known.</param>
/// <param name="DurationSeconds">Duration, when known.</param>
public sealed record MediaInfo(bool HasTrack, bool Playing, string Title, string Artist, string Album, double? PositionSeconds, double? DurationSeconds);

/// <summary>
/// A phone notification.
/// </summary>
/// <param name="Id">Stable id.</param>
/// <param name="App">App name.</param>
/// <param name="Title">Title (often the sender).</param>
/// <param name="Message">Text.</param>
/// <param name="Date">When it arrived on the phone.</param>
public sealed record NotificationInfo(string Id, string App, string Title, string Message, DateTimeOffset Date);

/// <summary>
/// An incoming call.
/// </summary>
/// <param name="Name">Caller name or number.</param>
public sealed record CallInfo(string Name);
