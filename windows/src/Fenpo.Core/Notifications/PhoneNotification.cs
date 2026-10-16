namespace Fenpo.Core.Notifications;

/// <summary>
/// What the phone says about a notification beyond its text.
/// </summary>
[Flags]
public enum NotificationFlags
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// Delivered silently on the phone.
    /// </summary>
    Silent = 1,

    /// <summary>
    /// Marked important by the sending app.
    /// </summary>
    Important = 2,

    /// <summary>
    /// Already on the phone when the connection started; never shown as a new toast.
    /// </summary>
    PreExisting = 4,
}

/// <summary>
/// Broad notification kinds the phone reports (ANCS categories, Android equivalents).
/// </summary>
public enum NotificationCategory
{
    /// <summary>Anything else.</summary>
    Other,

    /// <summary>A call is ringing.</summary>
    IncomingCall,

    /// <summary>A call was missed.</summary>
    MissedCall,

    /// <summary>A new voicemail.</summary>
    Voicemail,

    /// <summary>Messaging and social apps.</summary>
    Social,

    /// <summary>Calendar and reminders.</summary>
    Schedule,

    /// <summary>Mail.</summary>
    Email,

    /// <summary>News.</summary>
    News,

    /// <summary>Health and fitness.</summary>
    HealthAndFitness,

    /// <summary>Business and finance.</summary>
    BusinessAndFinance,

    /// <summary>Location based.</summary>
    Location,

    /// <summary>Entertainment.</summary>
    Entertainment,
}

/// <summary>
/// A notification mirrored from the phone. <see cref="Key"/> is unique within one connection only: ANCS restarts its
/// ids on every connection (S1), so nothing may persist it as a lasting identity.
/// </summary>
public sealed record PhoneNotification
{
    /// <summary>
    /// Gets the id for this connection (connection number and source id).
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the app's bundle or package id.
    /// </summary>
    public required string AppId { get; init; }

    /// <summary>
    /// Gets the app's display name, already cleaned.
    /// </summary>
    public required string AppName { get; init; }

    /// <summary>
    /// Gets the title (often the sender).
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets the subtitle.
    /// </summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>
    /// Gets the body text.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets when the phone received the notification, if it said.
    /// </summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>
    /// Gets the category.
    /// </summary>
    public NotificationCategory Category { get; init; }

    /// <summary>
    /// Gets the flags.
    /// </summary>
    public NotificationFlags Flags { get; init; }

    /// <summary>
    /// Gets the label of the positive action, or null when the notification has none.
    /// </summary>
    public string? PositiveActionLabel { get; init; }

    /// <summary>
    /// Gets the label of the negative action ("Clear"), or null when the notification has none.
    /// </summary>
    public string? NegativeActionLabel { get; init; }

    /// <summary>
    /// Gets the phone's own action buttons (Android). ANCS has only <see cref="PositiveActionLabel"/> and
    /// <see cref="NegativeActionLabel"/>.
    /// </summary>
    public IReadOnlyList<NotificationButton> Actions { get; init; } = [];

    /// <summary>
    /// Gets the sender photo or thumbnail as PNG (Android), or null.
    /// </summary>
    public byte[]? LargeIconPng { get; init; }

    /// <summary>
    /// Gets when Fenpo learned about it.
    /// </summary>
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// An action button the phone app put on a notification.
/// </summary>
/// <param name="Index">Index to send back.</param>
/// <param name="Title">Button text as the app gave it.</param>
/// <param name="AcceptsReply">True when the action takes typed text (a reply box).</param>
public sealed record NotificationButton(int Index, string Title, bool AcceptsReply);
