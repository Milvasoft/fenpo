namespace Fenpo.Core.Contacts;

/// <summary>
/// Whether the phone's contacts can be read.
/// </summary>
public enum ContactsAvailability
{
    /// <summary>No phone in reach, or not read yet.</summary>
    Unavailable,

    /// <summary>The phone refused: on the iPhone, "Sync Contacts" is off for this PC.</summary>
    NeedsPermission,

    /// <summary>Contacts were read.</summary>
    Ready,
}

/// <summary>
/// Names for phone numbers from the phone's contacts (iPhone: PBAP over Bluetooth). Kept in memory only.
/// </summary>
public interface IContactSource
{
    /// <summary>
    /// Raised after the contacts were read again.
    /// </summary>
    event EventHandler? ContactsChanged;

    /// <summary>
    /// Gets whether contacts can be read.
    /// </summary>
    ContactsAvailability Availability { get; }

    /// <summary>
    /// Gets contact names by phone number, keyed by <see cref="Messages.PhoneNumbers.Normalize"/>.
    /// </summary>
    IReadOnlyDictionary<string, string> NamesByNumber { get; }
}
