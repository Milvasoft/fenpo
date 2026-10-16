using System.Collections.Concurrent;
using Fenpo.Core.Devices;
using Fenpo.Core.Messages;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhoneMessage = Fenpo.Core.Messages.PhoneMessage;

namespace Fenpo.App.Services;

/// <summary>
/// SMS over the Fenpo channel (CAPABILITY_MESSAGES, messages.proto) for an Android phone in use whose Bluetooth has no
/// message access: conversations and their messages are asked for, new and changed messages arrive by themselves,
/// and texts are sent through the phone. Android does not let Fenpo mark messages read.
/// </summary>
public sealed class FenpoMessageSource(PhoneChannel channel, IPhoneStatusSource status, ILogger<FenpoMessageSource> logger) : IMessageSource, IHostedService
{
    private const int _conversations = 30;
    private static readonly TimeSpan _answerTimeout = TimeSpan.FromSeconds(10);

    private readonly PhoneChannel _channel = channel;
    private readonly IPhoneStatusSource _status = status;
    private readonly ILogger<FenpoMessageSource> _logger = logger;
    private readonly ConcurrentDictionary<long, SmsMessage> _messages = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<SmsMessages>> _threadRequests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SmsSendResult>> _sendRequests = new();
    private readonly SemaphoreSlim _listing = new(1, 1);
    private TaskCompletionSource<SmsConversations>? _conversationRequest;
    private MessagesAvailability _availability = MessagesAvailability.Unavailable;

    /// <inheritdoc/>
    public event EventHandler<MessagesAvailability>? AvailabilityChanged;

    /// <inheritdoc/>
    public event EventHandler? MessagesChanged;

    /// <inheritdoc/>
    public MessagesAvailability Availability => _availability;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => OnMessage(message);
        _channel.Changed += (sender, e) => UpdateAvailability();
        _status.ActiveChanged += (sender, kind) => UpdateAvailability();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PhoneMessage>> ListAsync(MessageFolder folder, int maxCount, CancellationToken cancellationToken = default)
    {
        if (_availability != MessagesAvailability.Ready)
        {
            return [];
        }

        // The page asks for each folder; one round of conversations and their newest messages serves both.
        await _listing.WaitAsync(cancellationToken);

        try
        {
            if (folder == MessageFolder.Inbox || _messages.IsEmpty)
            {
                await RefreshAsync(maxCount, cancellationToken);
            }
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("The phone did not answer for its messages in time");
        }
        finally
        {
            _listing.Release();
        }

        var outgoing = folder != MessageFolder.Inbox;
        return [.. _messages.Values
            .Where(m => m.Outgoing == outgoing && (folder != MessageFolder.Outbox || m.Status is SmsStatus.Pending or SmsStatus.Failed))
            .OrderByDescending(m => m.At?.ToDateTimeOffset())
            .Take(maxCount)
            .Select(ToPhoneMessage)];
    }

    /// <inheritdoc/>
    public Task<MessageContent?> GetAsync(string handle, CancellationToken cancellationToken = default) => Task.FromResult(IdOf(handle) is { } id && _messages.TryGetValue(id, out var message) ? new MessageContent(string.Empty, message.Address, message.Body) : null);

    /// <inheritdoc/>
    public Task<bool> SetReadAsync(string handle, bool read, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc/>
    public async Task<bool> SendAsync(string address, string text, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        var answer = new TaskCompletionSource<SmsSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sendRequests[requestId] = answer;

        try
        {
            if (!await _channel.SendAsync(new Envelope { SmsSend = new SmsSend { RequestId = requestId, Address = address, Body = text } }, cancellationToken))
            {
                return false;
            }

            var result = await answer.Task.WaitAsync(_answerTimeout, cancellationToken);
            _logger.LogInformation("SMS through the phone: {Accepted} {Reason}", result.Accepted, result.Reason);
            return result.Accepted;
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            _sendRequests.TryRemove(requestId, out _);
        }
    }

    private static string Handle(long id) => "sms:" + id;

    private static long? IdOf(string handle) => handle.StartsWith("sms:", StringComparison.Ordinal) && long.TryParse(handle.AsSpan(4), out var id) ? id : null;

    private static PhoneMessage ToPhoneMessage(SmsMessage message) => new()
    {
        Handle = Handle(message.Id),
        Folder = !message.Outgoing ? MessageFolder.Inbox : message.Status is SmsStatus.Pending or SmsStatus.Failed ? MessageFolder.Outbox : MessageFolder.Sent,
        Address = message.Address,
        Text = message.Body,
        Date = message.At?.ToDateTimeOffset().ToLocalTime() ?? DateTimeOffset.Now,
        IsRead = message.Read || message.Outgoing,
    };

    private async Task RefreshAsync(int maxCount, CancellationToken cancellationToken)
    {
        _conversationRequest = new TaskCompletionSource<SmsConversations>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.SendAsync(new Envelope { SmsSyncRequest = new SmsSyncRequest { Limit = _conversations } }, cancellationToken);
        var conversations = await _conversationRequest.Task.WaitAsync(_answerTimeout, cancellationToken);
        var perThread = (uint)Math.Clamp(maxCount / Math.Max(1, conversations.Conversations.Count), 5, 30);

        foreach (var conversation in conversations.Conversations)
        {
            var answer = new TaskCompletionSource<SmsMessages>(TaskCreationOptions.RunContinuationsAsynchronously);
            _threadRequests[conversation.ThreadId] = answer;
            await _channel.SendAsync(new Envelope { SmsMessagesRequest = new SmsMessagesRequest { ThreadId = conversation.ThreadId, Limit = perThread } }, cancellationToken);
            var messages = await answer.Task.WaitAsync(_answerTimeout, cancellationToken);
            _threadRequests.TryRemove(conversation.ThreadId, out _);

            foreach (var message in messages.Messages)
            {
                _messages[message.Id] = message;
            }
        }

        _logger.LogInformation("SMS over Fenpo: {Conversations} conversations, {Messages} messages", conversations.Conversations.Count, _messages.Count);
    }

    // Only for the Android phone in use, and only when it offers SMS (its "sync messages" setting and permission).
    private void UpdateAvailability()
    {
        var ready = _status.Active == PhoneKind.Android && _channel.IsConnected && _channel.Supports(Capability.Messages);
        var availability = ready ? MessagesAvailability.Ready : MessagesAvailability.Unavailable;

        if (availability != _availability)
        {
            _availability = availability;

            if (!ready)
            {
                _messages.Clear();
            }

            AvailabilityChanged?.Invoke(this, availability);
        }
    }

    private void OnMessage(ChannelMessage message)
    {
        switch (message.Envelope.BodyCase)
        {
            case Envelope.BodyOneofCase.SmsConversations:
                _conversationRequest?.TrySetResult(message.Envelope.SmsConversations);
                break;
            case Envelope.BodyOneofCase.SmsMessages when _threadRequests.TryGetValue(message.Envelope.SmsMessages.ThreadId, out var waiting):
                waiting.TrySetResult(message.Envelope.SmsMessages);
                break;
            case Envelope.BodyOneofCase.SmsSendResult when _sendRequests.TryGetValue(message.Envelope.SmsSendResult.RequestId, out var sending):
                sending.TrySetResult(message.Envelope.SmsSendResult);
                break;
            case Envelope.BodyOneofCase.SmsMessage:
                // A new message or a sent one's new status: the lists read again.
                _messages[message.Envelope.SmsMessage.Id] = message.Envelope.SmsMessage;
                MessagesChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
