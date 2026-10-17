using Fenpo.Core.Contacts;
using Fenpo.Core.Messages;

namespace Fenpo.App.Demo;

/// <summary>
/// The demo phone's messages and contacts. Sending only says yes (the page shows the sent message); nothing leaves the PC.
/// </summary>
public sealed class DemoMessageSource : IMessageSource, IContactSource
{
    private readonly Lock _lock = new();
    private readonly List<PhoneMessage> _messages;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoMessageSource"/> class.
    /// </summary>
    public DemoMessageSource()
    {
        var now = DemoMode.Now;
        _messages = [.. DemoContent.Current.Conversations.SelectMany((conversation, c) => conversation.Messages.Select((message, m) => new PhoneMessage
        {
            Handle = $"demo-{c}-{m}",
            Folder = message.Outgoing ? MessageFolder.Sent : MessageFolder.Inbox,
            Name = conversation.Name,
            Address = conversation.Number,
            Text = message.Text,
            Date = now - message.Ago,
            IsRead = message.Outgoing || message.Ago > TimeSpan.FromMinutes(10),
        }))];
        NamesByNumber = DemoContent.Current.Conversations.ToDictionary(c => PhoneNumbers.Normalize(c.Number), c => c.Name);
    }

    /// <inheritdoc/>
    public event EventHandler<MessagesAvailability>? AvailabilityChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler? MessagesChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler? ContactsChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public MessagesAvailability Availability => MessagesAvailability.Ready;

    /// <inheritdoc/>
    ContactsAvailability IContactSource.Availability => ContactsAvailability.Ready;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> NamesByNumber { get; }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PhoneMessage>> ListAsync(MessageFolder folder, int maxCount, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PhoneMessage>>([.. _messages.Where(m => m.Folder == folder).OrderByDescending(m => m.Date).Take(maxCount)]);
        }
    }

    /// <inheritdoc/>
    public Task<MessageContent?> GetAsync(string handle, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_messages.FirstOrDefault(m => m.Handle == handle) is { } message ? new MessageContent(message.Name, message.Address, message.Text) : null);
        }
    }

    /// <inheritdoc/>
    public Task<bool> SetReadAsync(string handle, bool read, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _messages.FindIndex(m => m.Handle == handle);

            if (index >= 0)
            {
                _messages[index] = _messages[index] with { IsRead = read };
            }
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> SendAsync(string address, string text, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
