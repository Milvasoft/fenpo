using System.Text.Json.Nodes;
using Fenpo.Core.Api;
using Fenpo.Core.Devices;
using Fenpo.Core.Media;
using Fenpo.Core.Notifications;
using Fenpo.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Fenpo.Core.Tests.Api;

public sealed class LocalApiServerTests : IAsyncLifetime
{
    private readonly IPhoneStatusSource _status = Substitute.For<IPhoneStatusSource>();
    private readonly IMediaSource _media = Substitute.For<IMediaSource>();
    private readonly NotificationStore _notifications = new();
    private readonly string _pipeName = $"fenpo-test-{Guid.NewGuid():N}";
    private LocalApiServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _status.Current.Returns(new PhoneStatus { State = PhoneConnectionState.Connected, Name = "bugiphone", BatteryPercent = 78, BluetoothConnected = true });
        _media.Current.Returns(new NowPlaying { Title = "Gece Yolculuğu", Artist = "Kara Bulut", State = PlaybackState.Playing, Duration = TimeSpan.FromMinutes(3) });
        _server = new LocalApiServer(_status, _media, _notifications, NullLogger<LocalApiServer>.Instance) { PipeName = _pipeName };
        return new ValueTask(_server.StartAsync(TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task ClientReadsStatusMediaAndNotifications()
    {
        _notifications.Add(new PhoneNotification { Key = "1:1", AppId = "net.whatsapp.WhatsApp", AppName = "WhatsApp", Title = "Selin", Message = "Sunumu attım", Date = DateTimeOffset.Now });
        await using var client = await FenpoClient.ConnectAsync(TimeSpan.FromSeconds(5), _pipeName, TestContext.Current.CancellationToken);

        var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);
        var media = await client.GetMediaStateAsync(TestContext.Current.CancellationToken);
        var recent = await client.GetRecentNotificationsAsync(5, TestContext.Current.CancellationToken);

        Assert.True(status.Connected);
        Assert.Equal("bugiphone", status.DeviceName);
        Assert.Equal(78, status.Battery);
        Assert.Equal(["bluetooth"], status.Links);
        Assert.True(media.Playing);
        Assert.Equal("Gece Yolculuğu", media.Title);
        Assert.Equal(180, media.DurationSeconds);
        var notification = Assert.Single(recent);
        Assert.Equal("Selin", notification.Title);
    }

    [Fact]
    public async Task MediaCommandsReachThePhone()
    {
        _media.SendAsync(MediaCommand.NextTrack, Arg.Any<CancellationToken>()).Returns(true);
        _media.SendAsync(MediaCommand.Pause, Arg.Any<CancellationToken>()).Returns(false);
        await using var client = await FenpoClient.ConnectAsync(TimeSpan.FromSeconds(5), _pipeName, TestContext.Current.CancellationToken);

        await client.NextAsync(TestContext.Current.CancellationToken);
        var refused = await Assert.ThrowsAsync<FenpoException>(() => client.PauseAsync(TestContext.Current.CancellationToken));

        await _media.Received(1).SendAsync(MediaCommand.NextTrack, Arg.Any<CancellationToken>());
        Assert.Equal(FenpoErrorCodes.PhoneUnavailable, refused.Code);
    }

    [Fact]
    public async Task AppOnlyMethodsSayWhatTheyNeed()
    {
        await using var client = await FenpoClient.ConnectAsync(TimeSpan.FromSeconds(5), _pipeName, TestContext.Current.CancellationToken);

        var ring = await Assert.ThrowsAsync<FenpoException>(() => client.RingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(FenpoErrorCodes.NeedsPhoneApp, ring.Code);
    }

    [Fact]
    public async Task EventsReachEveryClient()
    {
        await using var first = await FenpoClient.ConnectAsync(TimeSpan.FromSeconds(5), _pipeName, TestContext.Current.CancellationToken);
        await using var second = await FenpoClient.ConnectAsync(TimeSpan.FromSeconds(5), _pipeName, TestContext.Current.CancellationToken);
        var firstCall = new TaskCompletionSource<CallInfo>();
        var secondNotification = new TaskCompletionSource<NotificationInfo>();
        first.CallIncoming += (sender, call) => firstCall.TrySetResult(call);
        second.NotificationAdded += (sender, notification) => secondNotification.TrySetResult(notification);

        // Both clients are registered once each has answered a call.
        await first.GetStatusAsync(TestContext.Current.CancellationToken);
        await second.GetStatusAsync(TestContext.Current.CancellationToken);
        _notifications.Add(new PhoneNotification { Key = "1:9", AppId = "com.apple.mobilephone", AppName = "Telefon", Title = "Annem", Message = "Gelen Arama", Category = NotificationCategory.IncomingCall, Date = DateTimeOffset.Now });

        Assert.Equal("Annem", (await firstCall.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Name);
        Assert.Equal("Telefon", (await secondNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).App);
    }

    [Theory]
    [InlineData("not json", FenpoErrorCodes.ParseError)]
    [InlineData("""{"jsonrpc":"1.0","id":1,"method":"fenpo.status"}""", FenpoErrorCodes.InvalidRequest)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"fenpo.unknown"}""", FenpoErrorCodes.MethodNotFound)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"fenpo.notifications.recent","params":{"limit":0}}""", FenpoErrorCodes.InvalidParams)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"fenpo.clipboard.sendToPhone","params":{}}""", FenpoErrorCodes.InvalidParams)]
    public async Task BadRequestsGetJsonRpcErrors(string request, int code)
    {
        var response = JsonNode.Parse((await _server.HandleAsync(request, TestContext.Current.CancellationToken))!)!;

        Assert.Equal(code, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task NotificationsFromClientsGetNoResponse() =>
        Assert.Null(await _server.HandleAsync("""{"jsonrpc":"2.0","method":"fenpo.status"}""", TestContext.Current.CancellationToken));
}
