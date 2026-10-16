using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Fenpo.Core.Contacts;
using Fenpo.Core.Messages;
using Fenpo.Core.Notifications;
using Microsoft.UI.Dispatching;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Messages page: conversations from the phone's inbox and Fenpo's sent messages, a thread and a reply box.
/// </summary>
public sealed partial class MessagesViewModel : ObservableObject, IDisposable
{
    private const int _inboxCount = 150;
    private const int _sentCount = 50;
    private const int _lookUpLimit = 20;
    private static readonly TimeSpan _separatorGap = TimeSpan.FromHours(1);

    private readonly IMessageSource _source;
    private readonly MessageContacts _contacts;
    private readonly SentMessages _sentMessages;
    private readonly IContactSource _phoneBook;
    private readonly NotificationStore _notifications;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _reload;
    private readonly HashSet<string> _lookedUp = [];
    private List<PhoneMessage> _messages = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagesViewModel"/> class.
    /// </summary>
    /// <param name="source">Phone messages.</param>
    /// <param name="contacts">Names and numbers learned from full messages.</param>
    /// <param name="sentMessages">Messages sent from Fenpo, kept on this PC.</param>
    /// <param name="phoneBook">Names from the phone's contacts.</param>
    /// <param name="notifications">Phone notifications (Messages notifications name the sender).</param>
    /// <param name="localization">Strings.</param>
    public MessagesViewModel(IMessageSource source, MessageContacts contacts, SentMessages sentMessages, IContactSource phoneBook, NotificationStore notifications, LocalizationService localization)
    {
        _source = source;
        _contacts = contacts;
        _sentMessages = sentMessages;
        _phoneBook = phoneBook;
        _notifications = notifications;
        _localization = localization;

        // Event reports come in bursts (NewMessage, then MessageShift); one reload covers them.
        _reload = _dispatcher.CreateTimer();
        _reload.Interval = TimeSpan.FromSeconds(1);
        _reload.IsRepeating = false;
        _reload.Tick += (sender, e) => _ = LoadAsync();

        _source.AvailabilityChanged += OnAvailabilityChanged;
        _source.MessagesChanged += OnMessagesChanged;
        _phoneBook.ContactsChanged += OnContactsChanged;
        _notifications.Changed += OnContactsChanged;
        ApplyAvailability(_source.Availability);
        _ = LoadAsync();
    }

    /// <summary>Gets the conversations, newest first.</summary>
    public ObservableCollection<ConversationRowViewModel> Conversations { get; } = [];

    /// <summary>Gets the lines of the selected conversation.</summary>
    public ObservableCollection<BubbleItem> Bubbles { get; } = [];

    /// <summary>Gets or sets the selected conversation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ShowEmptyThread))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial ConversationRowViewModel? Selected { get; set; }

    /// <summary>Gets or sets the reply text.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = string.Empty;

    /// <summary>Gets or sets the line under the thread title (number, or why a reply is not possible).</summary>
    [ObservableProperty]
    public partial string ThreadSubtitle { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether a reply can be written in the selected conversation.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool CanReply { get; set; }

    /// <summary>Gets or sets the hint shown instead of the reply box.</summary>
    [ObservableProperty]
    public partial string ReplyHint { get; set; } = string.Empty;

    /// <summary>Gets or sets an error from the last send.</summary>
    [ObservableProperty]
    public partial string SendError { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether messages can be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowState), nameof(ShowEmptyThread))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsReady { get; set; }

    /// <summary>Gets or sets a value indicating whether the connection is being made.</summary>
    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    /// <summary>Gets or sets a value indicating whether the phone refused message access.</summary>
    [ObservableProperty]
    public partial bool NeedsPermission { get; set; }

    /// <summary>Gets or sets the state title when messages cannot be shown.</summary>
    [ObservableProperty]
    public partial string StateTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the state text when messages cannot be shown.</summary>
    [ObservableProperty]
    public partial string StateText { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether there are no conversations.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyThread))]
    public partial bool IsEmpty { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a send is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    /// <summary>Gets a value indicating whether a conversation is selected.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>Gets a value indicating whether the connection state panel is shown in place of the thread.</summary>
    public bool ShowState => !IsReady;

    /// <summary>Gets a value indicating whether the "pick a conversation" hint is shown.</summary>
    public bool ShowEmptyThread => IsReady && Selected is null;

    /// <inheritdoc/>
    public void Dispose()
    {
        _reload.Stop();
        _source.AvailabilityChanged -= OnAvailabilityChanged;
        _source.MessagesChanged -= OnMessagesChanged;
        _phoneBook.ContactsChanged -= OnContactsChanged;
        _notifications.Changed -= OnContactsChanged;
    }

    /// <summary>
    /// Starts a new conversation with a number and sends the first message.
    /// </summary>
    /// <param name="number">Recipient number.</param>
    /// <param name="text">Text.</param>
    /// <returns>True when the phone took the message.</returns>
    public async Task<bool> SendNewAsync(string number, string text)
    {
        if (!PhoneNumbers.IsDialable(number) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var sent = await _source.SendAsync(number.Trim(), text.Trim());

        if (sent)
        {
            _sentMessages.Add(number.Trim(), text.Trim());
        }

        SendError = sent ? string.Empty : _localization.GetString("Messages_SendFailed");
        return sent;
    }

    /// <summary>
    /// Gets a value indicating whether a number can receive a message.
    /// </summary>
    /// <param name="number">Number.</param>
    /// <returns>True for a phone number.</returns>
    public static bool IsDialable(string number) => PhoneNumbers.IsDialable(number);

    partial void OnSelectedChanged(ConversationRowViewModel? value)
    {
        SendError = string.Empty;
        ShowThread();

        if (value is not null)
        {
            _ = MarkReadAsync(value.Conversation);
            _ = LearnContactAsync(value.Conversation);
        }
    }

    private bool CanSend() => IsReady && CanReply && !IsSending && !string.IsNullOrWhiteSpace(Draft) && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (Selected is not { } selected)
        {
            return;
        }

        IsSending = true;
        var text = Draft.Trim();
        var sent = await _source.SendAsync(selected.Conversation.Address, text);
        IsSending = false;

        if (sent)
        {
            _sentMessages.Add(selected.Conversation.Address, text);
            Draft = string.Empty;
            SendError = string.Empty;
            Bubbles.Add(new BubbleItem(BubbleKind.Outgoing, text));
        }
        else
        {
            SendError = _localization.GetString("Messages_SendFailed");
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private void OnAvailabilityChanged(object? sender, MessagesAvailability availability) => _dispatcher.TryEnqueue(() =>
    {
        ApplyAvailability(availability);

        if (availability == MessagesAvailability.Ready)
        {
            _ = LoadAsync();
        }
    });

    private void OnContactsChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Rebuild);

    private void OnMessagesChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        _reload.Stop();
        _reload.Start();
    });

    private void ApplyAvailability(MessagesAvailability availability)
    {
        IsReady = availability == MessagesAvailability.Ready;
        IsConnecting = availability == MessagesAvailability.Connecting;
        NeedsPermission = availability == MessagesAvailability.NeedsPermission;
        (StateTitle, StateText) = availability switch
        {
            MessagesAvailability.Connecting => (_localization.GetString("Messages_ConnectingTitle"), _localization.GetString("Messages_ConnectingText")),
            MessagesAvailability.NeedsPermission => (_localization.GetString("Messages_PermissionTitle"), _localization.GetString("Messages_PermissionText")),
            _ => (_localization.GetString("Messages_UnavailableTitle"), _localization.GetString("Messages_UnavailableText")),
        };
    }

    private async Task LoadAsync()
    {
        if (_source.Availability != MessagesAvailability.Ready)
        {
            return;
        }

        var inbox = await _source.ListAsync(MessageFolder.Inbox, _inboxCount);
        var sent = await _source.ListAsync(MessageFolder.Sent, _sentCount);
        // The phone's sent folder may not keep what Fenpo sent; the local copies fill in, without doubles.
        var local = _sentMessages.All.Where(m => !sent.Any(s => PhoneNumbers.Normalize(s.Address) == PhoneNumbers.Normalize(m.Address) && s.Text == m.Text && (s.Date - m.Date).Duration() < TimeSpan.FromMinutes(5)));
        _messages = [.. inbox, .. sent, .. local];
        Rebuild();
        await LearnContactsAsync();
    }

    private void Rebuild()
    {
        var selectedKey = Selected?.Key;

        foreach (var (name, number) in SenderNames.Learn(_messages, _notifications.Items.Select(i => i.Notification)))
        {
            _contacts.Remember(name, number);
        }

        var conversations = Core.Messages.Conversations.Build(_messages, _contacts.All, _phoneBook.NamesByNumber);
        Conversations.Clear();

        foreach (var conversation in conversations)
        {
            Conversations.Add(new ConversationRowViewModel(conversation, _localization));
        }

        IsEmpty = Conversations.Count == 0;

        // Keep the open conversation open; its key changes once its number is known, so match by name too.
        var reselect = Conversations.FirstOrDefault(c => c.Key == selectedKey) ?? (selectedKey is null ? null : Conversations.FirstOrDefault(c => c.Name == Selected?.Name));

        if (!ReferenceEquals(reselect, Selected))
        {
            Selected = reselect;
        }
        else
        {
            ShowThread();
        }
    }

    private void ShowThread()
    {
        Bubbles.Clear();

        if (Selected is not { } selected)
        {
            CanReply = false;
            return;
        }

        var conversation = selected.Conversation;
        DateTimeOffset? previous = null;

        foreach (var message in conversation.Messages)
        {
            if (previous is null || message.Date - previous > _separatorGap || message.Date.LocalDateTime.Date != previous.Value.LocalDateTime.Date)
            {
                Bubbles.Add(new BubbleItem(BubbleKind.Day, DayText(message.Date)));
            }

            Bubbles.Add(new BubbleItem(message.IsOutgoing ? BubbleKind.Outgoing : BubbleKind.Incoming, message.Text));
            previous = message.Date;
        }

        CanReply = conversation.CanReply;
        // The number goes under the name only when the name is a contact, not the number itself.
        ThreadSubtitle = conversation.CanReply && PhoneNumbers.Normalize(conversation.Name) != PhoneNumbers.Normalize(conversation.Address)
            ? conversation.Address
            : _localization.GetString("Messages_Sms");
        ReplyHint = conversation.CanReply
            ? string.Empty
            : _lookedUp.Contains(conversation.Key) || conversation.Messages.All(m => m.IsOutgoing)
                ? _localization.GetString("Messages_NoReply")
                : _localization.GetString("Messages_FindingNumber");
    }

    private string DayText(DateTimeOffset date)
    {
        var local = date.LocalDateTime;
        var time = local.ToString("t", CultureInfo.CurrentCulture);

        if (local.Date == DateTime.Today)
        {
            return _localization.Format("Messages_TodayAt", time);
        }

        return local.Date == DateTime.Today.AddDays(-1)
            ? _localization.Format("Messages_YesterdayAt", time)
            : local.ToString("d MMMM, t", CultureInfo.CurrentCulture);
    }

    private async Task MarkReadAsync(Conversation conversation)
    {
        foreach (var message in conversation.Messages.Where(m => !m.IsOutgoing && !m.IsRead))
        {
            if (!await _source.SetReadAsync(message.Handle, true))
            {
                return;
            }
        }
    }

    private bool NeedsLookUp(Conversation conversation) => !conversation.CanReply || (PhoneNumbers.IsDialable(conversation.Name) && _phoneBook.Availability != ContactsAvailability.Ready);

    private async Task LearnContactsAsync()
    {
        // Sequential: MAP runs one operation at a time anyway.
        foreach (var conversation in Conversations.Select(c => c.Conversation).Where(NeedsLookUp).Take(_lookUpLimit).ToList())
        {
            await LearnContactAsync(conversation);
        }
    }

    private async Task LearnContactAsync(Conversation conversation)
    {
        // The listing has either the contact's name without the number (S2) or the number without the name; the
        // full message has both (vCard FN and TEL). One look-up per conversation.
        if (!NeedsLookUp(conversation) || !_lookedUp.Add(conversation.Key) || conversation.Messages.LastOrDefault(m => !m.IsOutgoing) is not { } received)
        {
            return;
        }

        var content = await _source.GetAsync(received.Handle);
        var name = content is not null && content.Name.Any(char.IsLetter) ? content.Name : conversation.Name;
        var number = content is not null && PhoneNumbers.IsDialable(content.Address) ? content.Address : conversation.Address;

        if (name.Any(char.IsLetter) && PhoneNumbers.IsDialable(number))
        {
            _contacts.Remember(name, number);
            Rebuild();
        }
        else if (Selected?.Key == conversation.Key)
        {
            ShowThread();
        }
    }
}
