namespace Fenpo.Core.Messages;

/// <summary>
/// Messages with one person, oldest first.
/// </summary>
/// <param name="Key">Grouping key: the normalized address, or the name when there is no address.</param>
/// <param name="Name">Display name.</param>
/// <param name="Address">Phone number for replies, or empty when not known yet.</param>
/// <param name="Messages">Messages, oldest first.</param>
public sealed record Conversation(string Key, string Name, string Address, IReadOnlyList<PhoneMessage> Messages)
{
    /// <summary>
    /// Gets the newest message.
    /// </summary>
    public PhoneMessage Last => Messages[^1];

    /// <summary>
    /// Gets the number of unread received messages.
    /// </summary>
    public int UnreadCount => Messages.Count(m => !m.IsOutgoing && !m.IsRead);

    /// <summary>
    /// Gets a value indicating whether a reply can be sent (needs a phone number, not a sender name or short code).
    /// </summary>
    public bool CanReply => PhoneNumbers.IsDialable(Address);
}

/// <summary>
/// Groups messages into conversations. The iPhone lists received messages under the contact's name and sent ones under
/// the number (S2), so known name and number pairs (from full messages) join the two sides.
/// </summary>
public static class Conversations
{
    /// <summary>
    /// Builds the conversation list, newest conversation first.
    /// </summary>
    /// <param name="messages">Messages from all folders.</param>
    /// <param name="contacts">Known contacts: name to phone number, learned from full messages.</param>
    /// <param name="phoneBook">Names by normalized number from the phone's contacts, if read.</param>
    /// <returns>Conversations.</returns>
    public static IReadOnlyList<Conversation> Build(IEnumerable<PhoneMessage> messages, IReadOnlyDictionary<string, string> contacts, IReadOnlyDictionary<string, string>? phoneBook = null)
    {
        var nameByNumber = contacts
            .Where(c => PhoneNumbers.IsDialable(c.Value))
            .GroupBy(c => PhoneNumbers.Normalize(c.Value))
            .ToDictionary(g => g.Key, g => g.First().Key);

        // The phone book is the better source: it has every contact, not only the ones seen in full messages.
        foreach (var (number, name) in phoneBook ?? new Dictionary<string, string>())
        {
            nameByNumber[number] = name;
        }

        return [.. messages
            .GroupBy(m => KeyOf(m, contacts))
            .Select(g =>
            {
                var ordered = g.OrderBy(m => m.Date).ToList();
                var name = ordered.Select(m => NameOf(m, nameByNumber)).LastOrDefault(n => n.Length > 0)
                    ?? ordered.Select(m => m.Name.Length > 0 ? m.Name : m.Address).Last();
                var address = ordered.Select(m => m.Address).LastOrDefault(PhoneNumbers.IsDialable)
                    ?? (contacts.TryGetValue(name, out var number) ? number : string.Empty);
                return new Conversation(g.Key, name, address, ordered);
            })
            .OrderByDescending(c => c.Last.Date)];
    }

    private static string KeyOf(PhoneMessage message, IReadOnlyDictionary<string, string> contacts)
    {
        if (PhoneNumbers.IsDialable(message.Address))
        {
            return PhoneNumbers.Normalize(message.Address);
        }

        if (contacts.TryGetValue(message.Name, out var number) && PhoneNumbers.IsDialable(number))
        {
            return PhoneNumbers.Normalize(number);
        }

        return (message.Name.Length > 0 ? message.Name : message.Address).Trim();
    }

    private static string NameOf(PhoneMessage message, Dictionary<string, string> nameByNumber)
    {
        if (message.Name.Length > 0 && message.Name != message.Address)
        {
            return message.Name;
        }

        return PhoneNumbers.IsDialable(message.Address) && nameByNumber.TryGetValue(PhoneNumbers.Normalize(message.Address), out var name)
            ? name
            : string.Empty;
    }
}
