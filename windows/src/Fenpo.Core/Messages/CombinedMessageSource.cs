namespace Fenpo.Core.Messages;

/// <summary>
/// Several message sources as one: the first that is ready is used (message access over Bluetooth first, then SMS
/// over the Fenpo channel for an Android phone without it); while none is, the first one's state shows.
/// </summary>
public sealed class CombinedMessageSource : IMessageSource
{
    private readonly IReadOnlyList<IMessageSource> _sources;

    /// <summary>
    /// Initializes a new instance of the <see cref="CombinedMessageSource"/> class.
    /// </summary>
    /// <param name="sources">Sources in order of preference.</param>
    public CombinedMessageSource(IReadOnlyList<IMessageSource> sources)
    {
        _sources = sources;

        foreach (var source in sources)
        {
            source.AvailabilityChanged += (sender, availability) => AvailabilityChanged?.Invoke(this, Availability);
            source.MessagesChanged += (sender, e) =>
            {
                if (source == Current)
                {
                    MessagesChanged?.Invoke(this, EventArgs.Empty);
                }
            };
        }
    }

    /// <inheritdoc/>
    public event EventHandler<MessagesAvailability>? AvailabilityChanged;

    /// <inheritdoc/>
    public event EventHandler? MessagesChanged;

    /// <inheritdoc/>
    public MessagesAvailability Availability => Current.Availability;

    private IMessageSource Current => _sources.FirstOrDefault(s => s.Availability == MessagesAvailability.Ready) ?? _sources[0];

    /// <inheritdoc/>
    public Task<IReadOnlyList<PhoneMessage>> ListAsync(MessageFolder folder, int maxCount, CancellationToken cancellationToken = default) => Current.ListAsync(folder, maxCount, cancellationToken);

    /// <inheritdoc/>
    public Task<MessageContent?> GetAsync(string handle, CancellationToken cancellationToken = default) => Current.GetAsync(handle, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> SetReadAsync(string handle, bool read, CancellationToken cancellationToken = default) => Current.SetReadAsync(handle, read, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> SendAsync(string address, string text, CancellationToken cancellationToken = default) => Current.SendAsync(address, text, cancellationToken);
}
