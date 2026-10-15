using Fenpo.App.Services;
using Fenpo.Core.Messages;
using Microsoft.UI.Xaml.Media;

namespace Fenpo.App.ViewModels;

/// <summary>
/// One conversation in the list on the left of the Messages page.
/// </summary>
public sealed class ConversationRowViewModel(Conversation conversation, LocalizationService localization)
{
    /// <summary>Gets the conversation.</summary>
    public Conversation Conversation { get; } = conversation;

    /// <summary>Gets the grouping key.</summary>
    public string Key => Conversation.Key;

    /// <summary>Gets the display name.</summary>
    public string Name => Conversation.Name;

    /// <summary>Gets the avatar letter.</summary>
    public string Initial { get; } = Avatar.Initial(conversation.Name);

    /// <summary>Gets the avatar fill.</summary>
    public Brush Fill { get; } = Avatar.Fill(conversation.Name);

    /// <summary>Gets the newest message, one line.</summary>
    public string Preview { get; } = conversation.Last.Text.ReplaceLineEndings(" ");

    /// <summary>Gets the newest message's time.</summary>
    public string Time { get; } = NotificationRowViewModel.RelativeTime(conversation.Last.Date, localization);

    /// <summary>Gets a value indicating whether there are unread messages.</summary>
    public bool HasUnread => Conversation.UnreadCount > 0;

    /// <summary>Gets a value indicating whether the time is shown (instead of the unread dot).</summary>
    public bool ShowTime => !HasUnread;
}

/// <summary>
/// One line of a conversation: a day separator or a message bubble.
/// </summary>
/// <param name="Kind">What the line is.</param>
/// <param name="Text">Separator or message text.</param>
public sealed record BubbleItem(BubbleKind Kind, string Text);

/// <summary>
/// Kinds of conversation lines.
/// </summary>
public enum BubbleKind
{
    /// <summary>Time separator.</summary>
    Day,

    /// <summary>Received message.</summary>
    Incoming,

    /// <summary>Sent message.</summary>
    Outgoing,
}
