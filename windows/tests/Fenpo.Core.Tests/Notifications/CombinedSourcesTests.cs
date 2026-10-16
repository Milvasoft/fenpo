using Fenpo.Core.Media;
using Fenpo.Core.Notifications;

namespace Fenpo.Core.Tests.Notifications;

public sealed class CombinedSourcesTests
{
    [Fact]
    public async Task KeysGetTheSourcePrefixAndActionsGoBackToTheSource()
    {
        var iphone = new FakeNotifications();
        var android = new FakeNotifications();
        var combined = new CombinedNotificationSource([("i:", iphone), ("a:", android)]);
        var added = new List<string>();
        combined.Added += (sender, n) => added.Add(n.Key);

        iphone.Raise("1:5");
        android.Raise("0|com.whatsapp|7");

        Assert.Equal(["i:1:5", "a:0|com.whatsapp|7"], added);
        Assert.True(await combined.RunActionAsync("a:0|com.whatsapp|7", 2, "ok", TestContext.Current.CancellationToken));
        Assert.Equal(("0|com.whatsapp|7", 2, "ok"), android.LastRun);
        Assert.Null(iphone.LastRun);
        Assert.False(await combined.PerformActionAsync("x:1", positive: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ResetOfOneSourceDetachesOnlyItsKeys()
    {
        var iphone = new FakeNotifications();
        var android = new FakeNotifications();
        var combined = new CombinedNotificationSource([("i:", iphone), ("a:", android)]);
        var store = new NotificationStore();
        combined.Added += (sender, n) => store.Add(n);
        combined.Reset += (sender, prefix) => store.Detach(prefix);

        iphone.Raise("1:5", "from iPhone");
        android.Raise("k", "from Android");
        android.RaiseReset();
        store.DropDetached();

        var left = Assert.Single(store.Items);
        Assert.Equal("i:1:5", left.Key);
    }

    [Fact]
    public void OnlyThePhoneInUseShowsAndTakesCommands()
    {
        var iphone = new FakeMedia();
        var android = new FakeMedia();
        var combined = new CombinedMediaSource([iphone, android]);
        var shown = new List<string>();
        combined.Changed += (sender, n) => shown.Add(n.Title);

        iphone.Publish(new NowPlaying { Title = "iPhone song", State = PlaybackState.Paused });
        android.Publish(new NowPlaying { Title = "Android song", State = PlaybackState.Playing });
        Assert.Equal("iPhone song", combined.Current.Title);

        combined.Use(1);
        Assert.Equal("Android song", combined.Current.Title);
        Assert.Equal(["iPhone song", "Android song"], shown);
    }

    [Fact]
    public void StoreShowsOnlyTheSourceInUseButKeepsTheOther()
    {
        var store = new NotificationStore();
        var arrived = new List<string>();
        store.Arrived += (sender, n) => arrived.Add(n.Key!);
        store.ActiveSource = "i:";

        store.Add(new PhoneNotification { Key = "i:1:5", AppId = "a", AppName = "A", Message = "iPhone" });
        store.Add(new PhoneNotification { Key = "a:k", AppId = "a", AppName = "A", Message = "Android" });

        Assert.Equal(["i:1:5"], arrived);
        Assert.Equal("i:1:5", Assert.Single(store.Items).Key);

        store.ActiveSource = "a:";
        Assert.Equal("a:k", Assert.Single(store.Items).Key);
    }

    private sealed class FakeNotifications : INotificationSource
    {
        public event EventHandler<PhoneNotification>? Added;

        public event EventHandler<PhoneNotification>? Modified;

        public event EventHandler<string>? Removed;

        public event EventHandler<string>? Reset;

        public (string Key, int Index, string? Reply)? LastRun { get; private set; }

        public void Raise(string key, string message = "hello") => Added?.Invoke(this, new PhoneNotification { Key = key, AppId = "app", AppName = "App", Message = message });

        public void RaiseReset() => Reset?.Invoke(this, string.Empty);

        public Task<bool> PerformActionAsync(string key, bool positive, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> RunActionAsync(string key, int index, string? reply, CancellationToken cancellationToken = default)
        {
            LastRun = (key, index, reply);
            return Task.FromResult(true);
        }

        public void Unused()
        {
            Modified?.Invoke(this, null!);
            Removed?.Invoke(this, string.Empty);
        }
    }

    private sealed class FakeMedia : IMediaSource
    {
        public event EventHandler<NowPlaying>? Changed;

        public NowPlaying Current { get; private set; } = NowPlaying.Empty;

        public void Publish(NowPlaying nowPlaying)
        {
            Current = nowPlaying;
            Changed?.Invoke(this, nowPlaying);
        }

        public Task<bool> SendAsync(MediaCommand command, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
