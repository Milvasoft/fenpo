namespace Fenpo.Core.Notifications;

/// <summary>
/// Several notification sources as one (the iPhone through ANCS, an Android phone through the Fenpo channel). Keys
/// get the source's prefix, so a reset of one source leaves the other's notifications alone, and actions go back to
/// the source a key came from.
/// </summary>
public sealed class CombinedNotificationSource : INotificationSource
{
    private readonly IReadOnlyList<(string Prefix, INotificationSource Source)> _sources;

    /// <summary>
    /// Initializes a new instance of the <see cref="CombinedNotificationSource"/> class.
    /// </summary>
    /// <param name="sources">Sources with distinct key prefixes, e.g. "i:" and "a:".</param>
    public CombinedNotificationSource(IReadOnlyList<(string Prefix, INotificationSource Source)> sources)
    {
        _sources = sources;

        foreach (var (prefix, source) in sources)
        {
            source.Added += (sender, notification) => Added?.Invoke(this, notification with { Key = prefix + notification.Key });
            source.Modified += (sender, notification) => Modified?.Invoke(this, notification with { Key = prefix + notification.Key });
            source.Removed += (sender, key) => Removed?.Invoke(this, prefix + key);
            source.Reset += (sender, keyPrefix) => Reset?.Invoke(this, prefix + keyPrefix);
        }
    }

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Added;

    /// <inheritdoc/>
    public event EventHandler<PhoneNotification>? Modified;

    /// <inheritdoc/>
    public event EventHandler<string>? Removed;

    /// <inheritdoc/>
    public event EventHandler<string>? Reset;

    /// <inheritdoc/>
    public Task<bool> PerformActionAsync(string key, bool positive, CancellationToken cancellationToken = default) => Find(key) is var (source, sourceKey) ? source.PerformActionAsync(sourceKey, positive, cancellationToken) : Task.FromResult(false);

    /// <inheritdoc/>
    public Task<bool> RunActionAsync(string key, int index, string? reply, CancellationToken cancellationToken = default) => Find(key) is var (source, sourceKey) ? source.RunActionAsync(sourceKey, index, reply, cancellationToken) : Task.FromResult(false);

    private (INotificationSource Source, string Key)? Find(string key)
    {
        foreach (var (prefix, source) in _sources)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return (source, key[prefix.Length..]);
            }
        }

        return null;
    }
}
