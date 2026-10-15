namespace Fenpo.Core.Messages;

/// <summary>
/// Reads and sends the phone's text messages (iPhone: MAP over Bluetooth; later the Fenpo app or Android).
/// </summary>
public interface IMessageSource
{
    /// <summary>
    /// Raised when <see cref="Availability"/> changes.
    /// </summary>
    event EventHandler<MessagesAvailability>? AvailabilityChanged;

    /// <summary>
    /// Raised when messages were added, moved or changed on the phone; lists should be read again.
    /// </summary>
    event EventHandler? MessagesChanged;

    /// <summary>
    /// Gets whether messages can be read right now.
    /// </summary>
    MessagesAvailability Availability { get; }

    /// <summary>
    /// Lists the newest messages of a folder, newest first.
    /// </summary>
    /// <param name="folder">Folder.</param>
    /// <param name="maxCount">Maximum number of messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The messages, or an empty list when unavailable.</returns>
    Task<IReadOnlyList<PhoneMessage>> ListAsync(MessageFolder folder, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the whole message with the sender's address.
    /// </summary>
    /// <param name="handle">Message handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message, or null when it could not be read.</returns>
    Task<MessageContent?> GetAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message read or unread on the phone.
    /// </summary>
    /// <param name="handle">Message handle.</param>
    /// <param name="read">True for read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the phone accepted it.</returns>
    Task<bool> SetReadAsync(string handle, bool read, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a text message through the phone.
    /// </summary>
    /// <param name="address">Recipient phone number.</param>
    /// <param name="text">Text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the phone took it for sending.</returns>
    Task<bool> SendAsync(string address, string text, CancellationToken cancellationToken = default);
}
