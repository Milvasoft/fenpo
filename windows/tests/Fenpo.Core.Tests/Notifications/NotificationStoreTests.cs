using Fenpo.Core.Notifications;

namespace Fenpo.Core.Tests.Notifications;

public sealed class NotificationStoreTests
{
    private static readonly DateTimeOffset _date = new(2026, 10, 15, 21, 4, 0, TimeSpan.FromHours(3));

    [Fact]
    public void NewNotificationArrivesOnce()
    {
        var store = new NotificationStore();
        var arrived = new List<StoredNotification>();
        store.Arrived += (sender, stored) => arrived.Add(stored);

        store.Add(Message("1:7"));

        var stored = Assert.Single(arrived);
        Assert.Equal("1:7", stored.Key);
        Assert.Equal(NotificationStore.Fingerprint(Message("1:7")), stored.Id);
        Assert.Single(store.Items);
    }

    [Fact]
    public void PreExistingDoesNotArrive()
    {
        var store = new NotificationStore();
        var arrived = 0;
        store.Arrived += (sender, stored) => arrived++;

        store.Add(Message("1:7") with { Flags = NotificationFlags.PreExisting });

        Assert.Equal(0, arrived);
        Assert.Single(store.Items);
    }

    [Fact]
    public void ReconnectRelinksSameNotificationToNewKey()
    {
        var store = new NotificationStore();
        var arrived = 0;
        store.Arrived += (sender, stored) => arrived++;
        store.Add(Message("1:7"));
        var id = store.Items[0].Id;

        store.Detach();
        Assert.Null(store.KeyOf(id));

        // ANCS gives every connection new keys and re-sends what is still on the phone as pre-existing.
        store.Add(Message("2:3") with { Flags = NotificationFlags.PreExisting });
        store.DropDetached();

        Assert.Equal(1, arrived);
        Assert.Equal("2:3", store.KeyOf(id));
        Assert.Single(store.Items);
    }

    [Fact]
    public void DropDetachedRemovesWhatThePhoneDidNotResend()
    {
        var store = new NotificationStore();
        store.Add(Message("1:7"));
        store.Add(Message("1:8") with { Message = "Ayrı bir mesaj" });

        store.Detach();
        store.Add(Message("2:1") with { Flags = NotificationFlags.PreExisting });
        store.DropDetached();

        var left = Assert.Single(store.Items);
        Assert.Equal("2:1", left.Key);
    }

    [Fact]
    public void RemoveRaisesDismissedWithStableId()
    {
        var store = new NotificationStore();
        var dismissed = new List<string>();
        store.Dismissed += (sender, id) => dismissed.Add(id);
        store.Add(Message("1:7"));
        var id = store.Items[0].Id;

        store.Remove("1:7");
        store.Remove("1:99");

        Assert.Equal([id], dismissed);
        Assert.Empty(store.Items);
    }

    [Fact]
    public void ModifyReplacesContentUnderSameKey()
    {
        var store = new NotificationStore();
        store.Add(Message("1:7"));

        store.Modify(Message("1:7") with { Message = "3 yeni mesaj" });

        var item = Assert.Single(store.Items);
        Assert.Equal("3 yeni mesaj", item.Notification.Message);
        Assert.Equal("1:7", item.Key);
    }

    [Fact]
    public void ItemsAreNewestFirst()
    {
        var store = new NotificationStore();
        store.Add(Message("1:1") with { Message = "eski", Date = _date });
        store.Add(Message("1:2") with { Message = "yeni", Date = _date.AddMinutes(5) });

        Assert.Equal(["yeni", "eski"], store.Items.Select(i => i.Notification.Message));
    }

    private static PhoneNotification Message(string key) => new()
    {
        Key = key,
        AppId = "net.whatsapp.WhatsApp",
        AppName = "WhatsApp",
        Title = "Selin",
        Message = "Sunumu attım, bakabilir misin?",
        Date = _date,
        Category = NotificationCategory.Social,
    };
}
