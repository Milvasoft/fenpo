using Fenpo.Core.Messages;

namespace Fenpo.Core.Tests.Messages;

public sealed class CombinedMessageSourceTests
{
    [Fact]
    public async Task TheFirstReadySourceIsUsed()
    {
        var map = new FakeSource("map");
        var fenpo = new FakeSource("fenpo");
        var combined = new CombinedMessageSource([map, fenpo]);
        var changes = new List<MessagesAvailability>();
        combined.AvailabilityChanged += (sender, availability) => changes.Add(availability);

        Assert.Equal(MessagesAvailability.Unavailable, combined.Availability);

        fenpo.Set(MessagesAvailability.Ready);
        Assert.Equal("fenpo", (await combined.ListAsync(MessageFolder.Inbox, 10, TestContext.Current.CancellationToken))[0].Handle);

        map.Set(MessagesAvailability.Ready);
        Assert.Equal("map", (await combined.ListAsync(MessageFolder.Inbox, 10, TestContext.Current.CancellationToken))[0].Handle);
        Assert.Equal([MessagesAvailability.Ready, MessagesAvailability.Ready], changes);
    }

    [Fact]
    public void WhileNoneIsReadyTheFirstOnesStateShows()
    {
        var map = new FakeSource("map");
        var combined = new CombinedMessageSource([map, new FakeSource("fenpo")]);

        map.Set(MessagesAvailability.NeedsPermission);

        Assert.Equal(MessagesAvailability.NeedsPermission, combined.Availability);
    }

    private sealed class FakeSource(string name) : IMessageSource
    {
        public event EventHandler<MessagesAvailability>? AvailabilityChanged;

        public event EventHandler? MessagesChanged;

        public MessagesAvailability Availability { get; private set; } = MessagesAvailability.Unavailable;

        public void Set(MessagesAvailability availability)
        {
            Availability = availability;
            AvailabilityChanged?.Invoke(this, availability);
            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<IReadOnlyList<PhoneMessage>> ListAsync(MessageFolder folder, int maxCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PhoneMessage>>([new PhoneMessage { Handle = name }]);

        public Task<MessageContent?> GetAsync(string handle, CancellationToken cancellationToken = default) => Task.FromResult<MessageContent?>(null);

        public Task<bool> SetReadAsync(string handle, bool read, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> SendAsync(string address, string text, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
