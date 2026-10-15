namespace Fenpo.Core.Messages;

/// <summary>
/// Message folders every message source knows.
/// </summary>
public enum MessageFolder
{
    /// <summary>Received messages.</summary>
    Inbox,

    /// <summary>Sent messages. On the iPhone only the ones Fenpo sent (S2).</summary>
    Sent,

    /// <summary>Messages waiting to be sent.</summary>
    Outbox,
}

/// <summary>
/// Whether messages can be read right now.
/// </summary>
public enum MessagesAvailability
{
    /// <summary>No phone in reach.</summary>
    Unavailable,

    /// <summary>Connecting to the phone's message service.</summary>
    Connecting,

    /// <summary>The phone refused: on the iPhone, "Show Message Notifications" is off for this PC.</summary>
    NeedsPermission,

    /// <summary>Messages can be listed, read and sent.</summary>
    Ready,
}

/// <summary>
/// One SMS or iMessage as a list entry. The text may be shortened; <see cref="IMessageSource.GetAsync"/> gives all of it.
/// </summary>
public sealed record PhoneMessage
{
    /// <summary>
    /// Gets the source's handle for the message (MAP: 15 hex characters, not stable across connections).
    /// </summary>
    public required string Handle { get; init; }

    /// <summary>
    /// Gets the folder.
    /// </summary>
    public MessageFolder Folder { get; init; }

    /// <summary>
    /// Gets the other party's name, or their address when the phone has no name for them.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the other party's address (phone number, e-mail, short code), when the phone gives one.
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// Gets the text, possibly shortened.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets when it was sent or received.
    /// </summary>
    public DateTimeOffset Date { get; init; }

    /// <summary>
    /// Gets a value indicating whether it was read.
    /// </summary>
    public bool IsRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user sent it.
    /// </summary>
    public bool IsOutgoing => Folder != MessageFolder.Inbox;
}

/// <summary>
/// A whole message as the phone stores it.
/// </summary>
/// <param name="Name">Sender name from the phone's contacts, or empty.</param>
/// <param name="Address">Sender phone number or address, or empty.</param>
/// <param name="Text">Full text.</param>
public sealed record MessageContent(string Name, string Address, string Text);
