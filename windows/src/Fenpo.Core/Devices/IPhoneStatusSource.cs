namespace Fenpo.Core.Devices;

/// <summary>
/// Publishes the phone's status as it changes.
/// </summary>
public interface IPhoneStatusSource
{
    /// <summary>
    /// Raised after <see cref="Current"/> changed.
    /// </summary>
    event EventHandler<PhoneStatus>? Changed;

    /// <summary>
    /// Raised when the phone in use changed.
    /// </summary>
    event EventHandler<PhoneKind>? ActiveChanged;

    /// <summary>
    /// Gets the status of the phone in use.
    /// </summary>
    PhoneStatus Current { get; }

    /// <summary>
    /// Gets which phone is in use.
    /// </summary>
    PhoneKind Active { get; }

    /// <summary>
    /// Gets the status of each phone, in use or not.
    /// </summary>
    /// <param name="kind">Phone.</param>
    /// <returns>Its latest status.</returns>
    PhoneStatus StatusOf(PhoneKind kind);

    /// <summary>
    /// Makes a phone the one in use and remembers the choice.
    /// </summary>
    /// <param name="kind">Phone.</param>
    void Use(PhoneKind kind);
}
