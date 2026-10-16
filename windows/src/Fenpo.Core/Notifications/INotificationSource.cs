namespace Fenpo.Core.Notifications;

/// <summary>
/// A stream of phone notifications. iPhone: ANCS; Android: the Fenpo channel (CAPABILITY_NOTIFICATION_MIRROR).
/// </summary>
public interface INotificationSource
{
    /// <summary>
    /// Raised for a new notification, including the ones already on the phone when it connects.
    /// </summary>
    event EventHandler<PhoneNotification>? Added;

    /// <summary>
    /// Raised when an existing notification's content changed.
    /// </summary>
    event EventHandler<PhoneNotification>? Modified;

    /// <summary>
    /// Raised with the key of a notification removed on the phone.
    /// </summary>
    event EventHandler<string>? Removed;

    /// <summary>
    /// Raised when the connection to the phone ended, with the prefix of the keys that are now invalid (empty for all of
    /// this source's keys).
    /// </summary>
    event EventHandler<string>? Reset;

    /// <summary>
    /// Performs the notification's positive or negative action on the phone.
    /// </summary>
    /// <param name="key">Notification key.</param>
    /// <param name="positive">True for the positive action, false for the negative one (clear).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the phone accepted the action.</returns>
    Task<bool> PerformActionAsync(string key, bool positive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Presses one of the phone's own action buttons (<see cref="PhoneNotification.Actions"/>), with a typed reply when
    /// the action takes one. Only Android has them.
    /// </summary>
    /// <param name="key">Notification key.</param>
    /// <param name="index">Action index.</param>
    /// <param name="reply">Reply text, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the phone got the request.</returns>
    Task<bool> RunActionAsync(string key, int index, string? reply, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
