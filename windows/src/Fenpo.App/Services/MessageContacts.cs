using System.Text.Json;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// Names and numbers learned from full messages. The iPhone lists received messages under the contact's name only (S2),
/// so the number needed for a reply is remembered here, on this PC only.
/// </summary>
public sealed class MessageContacts(ILogger<MessageContacts> logger)
{
    private readonly ILogger<MessageContacts> _logger = logger;
    private readonly string _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "message-contacts.json");
    private Dictionary<string, string>? _contacts;

    /// <summary>
    /// Gets the known contacts: name to phone number.
    /// </summary>
    public IReadOnlyDictionary<string, string> All => Load();

    /// <summary>
    /// Remembers a number for a name.
    /// </summary>
    /// <param name="name">Name as the phone shows it.</param>
    /// <param name="number">Phone number.</param>
    public void Remember(string name, string number)
    {
        var contacts = Load();

        if (name.Length == 0 || (contacts.TryGetValue(name, out var known) && known == number))
        {
            return;
        }

        contacts[name] = number;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(contacts));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Saving message contacts failed");
        }
    }

    private Dictionary<string, string> Load()
    {
        if (_contacts is not null)
        {
            return _contacts;
        }

        try
        {
            _contacts = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _logger.LogWarning(exception, "Message contacts could not be read; starting empty");
            _contacts = [];
        }

        return _contacts;
    }
}
