using System.Text;
using Fenpo.Bluetooth.Map;
using Fenpo.Bluetooth.Obex;
using Fenpo.Core.Messages;

namespace Fenpo.Bluetooth.Tests.Map;

/// <summary>
/// Plays the iPhone's side of the recorded S2 session back to <see cref="ObexClient"/>.
/// </summary>
public sealed class ObexReplayTests
{
    private static readonly IReadOnlyList<FixtureEntry> _session = FixtureEntry.Load(Path.Combine("Map", "obex-mas-mns-session.jsonl"));

    [Fact]
    public async Task MasSessionReplays()
    {
        var phone = new MemoryStream([.. _session.Where(e => e.Source == "mas" && e.Kind == "rx").SelectMany(e => e.Data)]);
        var sent = new MemoryStream();
        var client = new ObexClient(phone, sent);
        var opcodes = new List<byte>();
        client.PacketObserved += (outgoing, packet) =>
        {
            if (outgoing)
            {
                opcodes.Add(packet[0]);
            }
        };

        var connect = await client.ConnectAsync(Convert.FromHexString("BB582B40420C11DBB0DE0800200C9A66"), TestContext.Current.CancellationToken);
        Assert.Equal(ObexResponse.Success, connect.Code);
        Assert.Equal(4000, client.PeerMaxPacket);
        Assert.NotNull(client.ConnectionId);

        Assert.Equal(ObexResponse.Success, (await client.SetPathAsync("telecom", cancellationToken: TestContext.Current.CancellationToken)).Code);
        Assert.Equal(ObexResponse.Success, (await client.SetPathAsync("msg", cancellationToken: TestContext.Current.CancellationToken)).Code);

        var folders = await client.GetAsync([ObexHeader.Type("x-obex/folder-listing")], TestContext.Current.CancellationToken);
        Assert.Contains("inbox", Encoding.UTF8.GetString(folders.Body), StringComparison.Ordinal);

        var inbox = await client.GetAsync([ObexHeader.Type("x-bt/MAP-msg-listing"), ObexHeader.Name("inbox")], TestContext.Current.CancellationToken);
        Assert.Equal(10, MapListing.Parse(inbox.Body, MessageFolder.Inbox).Count);
        Assert.NotNull(MapParameters.Find(inbox.ApplicationParameters, MapParameters.MseTime));

        var sentFolder = await client.GetAsync([ObexHeader.Type("x-bt/MAP-msg-listing"), ObexHeader.Name("sent")], TestContext.Current.CancellationToken);
        Assert.Empty(MapListing.Parse(sentFolder.Body, MessageFolder.Sent));

        Assert.Equal(ObexResponse.Success, (await client.PutAsync([0x30], [ObexHeader.Type("x-bt/MAP-NotificationRegistration")], TestContext.Current.CancellationToken)).Code);

        var message = await client.GetAsync([ObexHeader.Type("x-bt/message"), ObexHeader.Name("446DE16F42E043F")], TestContext.Current.CancellationToken);
        Assert.StartsWith("Siparişiniz teslim edildi.", BMessage.Parse(Encoding.UTF8.GetString(message.Body)).Text, StringComparison.Ordinal);

        Assert.Equal(opcodes, _session.Where(e => e.Source == "mas" && e.Kind == "tx").Take(opcodes.Count).Select(e => e.Data[0]));
    }

    [Fact]
    public void NotificationServerPacketsDecode()
    {
        var received = _session.Where(e => e.Source == "mns" && e.Kind == "rx").ToList();
        var connect = ObexPacket.Decode(received[0].Data, 4);

        Assert.Equal(ObexOpcode.Connect, connect.Code);
        Assert.Equal(Convert.FromHexString("BB582B41420C11DBB0DE0800200C9A66"), connect.Header(ObexHeaderId.Target)?.Value);

        var types = received.Skip(1)
            .Select(e => ObexPacket.Decode(e.Data, 0))
            .SelectMany(p => MapEvent.Parse([.. p.BodyBytes()]))
            .Select(e => e.Type)
            .ToList();

        Assert.Equal(["NewMessage", "MessageShift", "SendingSuccess"], types);
    }

    [Fact]
    public void PacketsRoundTrip()
    {
        var lastRequest = new Dictionary<string, byte>();

        foreach (var entry in _session)
        {
            // Requests go from the client: tx on MAS, rx on MNS. A response's fields depend on its request.
            var isRequest = (entry.Source == "mas") == (entry.Kind == "tx");
            var code = entry.Data[0];
            var fieldLength = isRequest
                ? code switch { ObexOpcode.Connect => 4, ObexOpcode.SetPath => 2, _ => 0 }
                : lastRequest.GetValueOrDefault(entry.Source) == ObexOpcode.Connect ? 4 : 0;

            if (isRequest)
            {
                lastRequest[entry.Source] = code;
            }

            Assert.Equal(entry.Data, ObexPacket.Decode(entry.Data, fieldLength).Encode());
        }
    }

    [Fact]
    public void TruncatedHeaderIsRejected() => Assert.Throws<InvalidDataException>(() => ObexPacket.Decode([0xA0, 0x00, 0x08, 0x48, 0x00, 0x20, 0x41, 0x42], 0));
}
