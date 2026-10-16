using System.Text.Json;
using Fenpo.Core.Messages;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// Messages sent from Fenpo, kept on this PC only. The iPhone's MAP sent folder came back empty after sending
/// (it held the message in S2), so without this a sent message vanished from the conversation on the next reload.
/// </summary>
public sealed class SentMessages(ILogger<SentMessages> logger)
{
    private const int _capacity = 500;

    private readonly ILogger<SentMessages> _logger = logger;
    private readonly string _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "sent-messages.json");
    private List<SentMessage>? _messages;

    /// <summary>
    /// Gets the sent messages as phone messages of the Sent folder.
    /// </summary>
    public IReadOnlyList<PhoneMessage> All => [.. Load().Select((m, i) => new PhoneMessage
    {
        Handle = $"fenpo-{i}",
        Folder = MessageFolder.Sent,
        Name = m.Address,
        Address = m.Address,
        Text = m.Text,
        Date = m.Date,
        IsRead = true,
    })];

    /// <summary>
    /// Records a message the phone took for sending.
    /// </summary>
    /// <param name="address">Recipient number.</param>
    /// <param name="text">Text.</param>
    public void Add(string address, string text)
    {
        var messages = Load();
        messages.Add(new SentMessage(address, text, DateTimeOffset.Now));

        if (messages.Count > _capacity)
        {
            messages.RemoveRange(0, messages.Count - _capacity);
        }

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(messages));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Saving sent messages failed");
        }
    }

    private List<SentMessage> Load()
    {
        if (_messages is not null)
        {
            return _messages;
        }

        try
        {
            _messages = File.Exists(_path) ? JsonSerializer.Deserialize<List<SentMessage>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _logger.LogWarning(exception, "Sent messages could not be read; starting empty");
            _messages = [];
        }

        return _messages;
    }

    private sealed record SentMessage(string Address, string Text, DateTimeOffset Date);
}
