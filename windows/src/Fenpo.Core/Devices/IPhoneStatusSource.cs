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
    /// Gets the latest status.
    /// </summary>
    PhoneStatus Current { get; }
}
