using Fenpo.Core.Notifications;

namespace Fenpo.Core.Messages;

/// <summary>
/// Learns contact names for numbers from the phone's Messages notifications. The iPhone lists a saved contact's SMS
/// under the bare number and leaves the name out of the full message too, but its notification for the same message
/// has the contact name as the title. Matching text and time ties the two together.
/// </summary>
public static class SenderNames
{
    /// <summary>
    /// The iOS Messages app.
    /// </summary>
    public const string MessagesAppId = "com.apple.MobileSMS";

    private static readonly TimeSpan _window = TimeSpan.FromMinutes(10);
    private const int _compareLength = 40;

    /// <summary>
    /// Finds names for received messages that only have a number.
    /// </summary>
    /// <param name="messages">Messages from the phone.</param>
    /// <param name="notifications">Notifications from the phone.</param>
    /// <returns>Name to number for every match.</returns>
    public static IReadOnlyDictionary<string, string> Learn(IEnumerable<PhoneMessage> messages, IEnumerable<PhoneNotification> notifications)
    {
        var candidates = notifications
            .Where(n => n.AppId == MessagesAppId && n.Title.Any(char.IsLetter) && !PhoneNumbers.IsDialable(n.Title) && n.Message.Length > 0)
            .ToList();
        var names = new Dictionary<string, string>();

        foreach (var message in messages.Where(m => !m.IsOutgoing && PhoneNumbers.IsDialable(m.Address) && !m.Name.Any(char.IsLetter)))
        {
            var text = Start(message.Text);

            if (text.Length == 0)
            {
                continue;
            }

            var match = candidates.FirstOrDefault(n => Start(n.Message) == text && ((n.Date ?? n.ReceivedAt) - message.Date).Duration() < _window);

            if (match is not null)
            {
                names.TryAdd(match.Title.Trim(), message.Address);
            }
        }

        return names;
    }

    // Listings and notifications shorten long texts differently; the start is enough to tell messages apart.
    private static string Start(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > _compareLength ? flat[.._compareLength] : flat;
    }
}
