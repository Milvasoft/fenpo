using System.Security.Cryptography;
using System.Text;

namespace Fenpo.Core.Notifications;

/// <summary>
/// A notification as Fenpo keeps it: stable <see cref="Id"/> across reconnects, current source key, content.
/// </summary>
/// <param name="Id">Stable id derived from the content (app, title, message, date).</param>
/// <param name="Key">Source key of the current connection, or null when the phone dropped it.</param>
/// <param name="Notification">Latest content.</param>
public sealed record StoredNotification(string Id, string? Key, PhoneNotification Notification);

/// <summary>
/// Keeps the notifications currently on the phone. ANCS keys change with every connection (S1), so items are identified by
/// a content fingerprint and re-linked to the new key when the phone re-sends them as pre-existing.
/// </summary>
public sealed class NotificationStore
{
    private readonly Lock _lock = new();
    private readonly List<StoredNotification> _items = [];

    /// <summary>
    /// Raised after the list changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised for a notification that is new to Fenpo (not a pre-existing one re-sent after a reconnect).
    /// </summary>
    public event EventHandler<StoredNotification>? Arrived;

    /// <summary>
    /// Raised with the id of a notification removed on the phone.
    /// </summary>
    public event EventHandler<string>? Dismissed;

    /// <summary>
    /// Gets a snapshot, newest first.
    /// </summary>
    public IReadOnlyList<StoredNotification> Items
    {
        get
        {
            lock (_lock)
            {
                return [.. _items.OrderByDescending(i => i.Notification.Date ?? i.Notification.ReceivedAt)];
            }
        }
    }

    /// <summary>
    /// Computes the stable id of a notification.
    /// </summary>
    /// <param name="notification">Notification.</param>
    /// <returns>16 hex characters.</returns>
    public static string Fingerprint(PhoneNotification notification)
    {
        var text = $"{notification.AppId}\n{notification.Title}\n{notification.Subtitle}\n{notification.Message}\n{notification.Date:O}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>
    /// Adds or re-links a notification.
    /// </summary>
    /// <param name="notification">Notification from the source.</param>
    public void Add(PhoneNotification notification)
    {
        var id = Fingerprint(notification);
        StoredNotification stored;
        bool isNew;

        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == id);
            isNew = index < 0;
            stored = new StoredNotification(id, notification.Key, notification);

            if (isNew)
            {
                _items.Add(stored);
            }
            else
            {
                _items[index] = stored with { Notification = notification with { ReceivedAt = _items[index].Notification.ReceivedAt } };
            }
        }

        if (isNew && !notification.Flags.HasFlag(NotificationFlags.PreExisting))
        {
            Arrived?.Invoke(this, stored);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates a notification whose content changed.
    /// </summary>
    /// <param name="notification">Notification from the source.</param>
    public void Modify(PhoneNotification notification)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.Key == notification.Key);
            _items.Add(new StoredNotification(Fingerprint(notification), notification.Key, notification));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes the notification with this source key.
    /// </summary>
    /// <param name="key">Source key.</param>
    public void Remove(string key)
    {
        string? id;

        lock (_lock)
        {
            id = _items.FirstOrDefault(i => i.Key == key)?.Id;
            _items.RemoveAll(i => i.Key == key);
        }

        if (id is not null)
        {
            Dismissed?.Invoke(this, id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Marks keys as stale after a disconnect; items stay until the phone re-sends or drops them.
    /// </summary>
    /// <param name="keyPrefix">Only keys with this prefix (one source's); empty for all.</param>
    public void Detach(string keyPrefix = "")
    {
        lock (_lock)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key?.StartsWith(keyPrefix, StringComparison.Ordinal) ?? false)
                {
                    _items[i] = _items[i] with { Key = null };
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Drops items the phone did not re-send after reconnecting (called once the pre-existing burst is over).
    /// </summary>
    public void DropDetached()
    {
        int removed;

        lock (_lock)
        {
            removed = _items.RemoveAll(i => i.Key is null);
        }

        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the current source key of a stored notification.
    /// </summary>
    /// <param name="id">Stable id.</param>
    /// <returns>The key, or null when it is not on the phone right now.</returns>
    public string? KeyOf(string id)
    {
        lock (_lock)
        {
            return _items.FirstOrDefault(i => i.Id == id)?.Key;
        }
    }
}
