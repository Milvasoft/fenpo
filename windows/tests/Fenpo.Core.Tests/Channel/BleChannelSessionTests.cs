using Fenpo.Core.Channel;
using Fenpo.Protocol.V1;
using Google.Protobuf;

namespace Fenpo.Core.Tests.Channel;

public sealed class BleChannelSessionTests
{
    private static readonly PairedAppDevice _phone = new("phone-1", "bugiphone", Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    private static readonly PairedAppDevice _other = new("phone-2", "başka", Enumerable.Range(100, 32).Select(i => (byte)i).ToArray());
    private static readonly PcIdentity _pc = new("pc-1", "BUGRAKOSEN", "0.1.0", "Windows 11 26300", string.Empty);

    [Fact]
    public void HelloIsAnsweredWithHelloThenPcStatus()
    {
        var (session, sent) = Create();

        session.OnChunk(ChunkFromPhone(_phone, 1, new Envelope { Id = 5, Hello = new Hello { DeviceId = "phone-1", Platform = DevicePlatform.Ios, OsVersion = "iOS 27.0.1" } })[0]);

        var replies = Open(sent);
        Assert.Equal(2, replies.Count);
        Assert.Equal(Envelope.BodyOneofCase.Hello, replies[0].BodyCase);
        Assert.Equal(5ul, replies[0].ReplyTo);
        Assert.Equal("BUGRAKOSEN", replies[0].Hello.DeviceName);
        Assert.Equal(DevicePlatform.Windows, replies[0].Hello.Platform);
        Assert.True(replies[1].PcStatus.NotificationsSubscribed);
        Assert.False(replies[1].PcStatus.MessagesAvailable);
        Assert.Equal(_phone, session.Device);
    }

    [Fact]
    public void PingIsAnsweredWithPongAcrossChunks()
    {
        var (session, sent) = Create(payloadLength: 8);

        foreach (var chunk in ChunkFromPhone(_phone, 1, new Envelope { Id = 9, Ping = new Ping { Nonce = 1234567890123 } }, payloadLength: 8))
        {
            session.OnChunk(chunk);
        }

        var pong = Assert.Single(Open(sent, payloadLength: 8));
        Assert.Equal(1234567890123ul, pong.Pong.Nonce);
        Assert.Equal(9ul, pong.ReplyTo);
    }

    [Fact]
    public void TheKeyThatOpensPicksThePhone()
    {
        var (session, _) = Create();

        Assert.Null(session.OnChunk(ChunkFromPhone(_other, 1, new Envelope { Ping = new Ping() })[0]));

        Assert.Equal(_other, session.Device);
    }

    [Fact]
    public void ReplaysAndUnknownKeysAreDropped()
    {
        var (session, sent) = Create();
        var ping = ChunkFromPhone(_phone, 3, new Envelope { Ping = new Ping { Nonce = 1 } })[0];
        var stranger = new PairedAppDevice("x", "x", new byte[32]);

        Assert.Null(session.OnChunk(ping));
        Assert.Equal("replayed or old counter", session.OnChunk(ping));
        Assert.Equal("replayed or old counter", session.OnChunk(ChunkFromPhone(_phone, 2, new Envelope { Ping = new Ping() })[0]));
        Assert.NotNull(session.OnChunk(ChunkFromPhone(stranger, 9, new Envelope { Ping = new Ping() })[0]));
        Assert.Single(Open(sent));
    }

    [Fact]
    public void NothingIsSentBeforeAPhoneIsBound()
    {
        var (session, sent) = Create();

        Assert.False(session.SendPcStatus());
        Assert.Empty(sent);
    }

    [Fact]
    public void SendCountersIncrease()
    {
        var (session, sent) = Create();
        session.OnChunk(ChunkFromPhone(_phone, 1, new Envelope { Ping = new Ping() })[0]);
        session.SendPcStatus();

        var counters = sent.Select(c => BleSeal.TryOpen(_phone.BleKey, c[BleChunks.HeaderLength..], BleSender.Pc, out _, out var counter) ? counter : 0ul).ToList();

        Assert.Equal([1ul, 2ul], counters);
    }

    private static (BleChannelSession Session, List<byte[]> Sent) Create(int payloadLength = 512)
    {
        var session = new BleChannelSession(new Devices(), new Counters(), _pc, () => new PcStatus { NotificationsSubscribed = true }, payloadLength);
        var sent = new List<byte[]>();
        session.ChunkReady += (sender, chunk) => sent.Add(chunk);
        return (session, sent);
    }

    private static IReadOnlyList<byte[]> ChunkFromPhone(PairedAppDevice device, ulong counter, Envelope envelope, int payloadLength = 512) => BleChunks.Split(BleSeal.Seal(device.BleKey, BleSender.Phone, counter, envelope.ToByteArray()), payloadLength);

    private static List<Envelope> Open(List<byte[]> chunks, int payloadLength = 512)
    {
        var assembler = new BleChunkAssembler();
        var envelopes = new List<Envelope>();

        foreach (var chunk in chunks)
        {
            if (assembler.TryAdd(chunk, out var message) && BleSeal.TryOpen(_phone.BleKey, message, BleSender.Pc, out var plaintext, out _))
            {
                envelopes.Add(Envelope.Parser.ParseFrom(plaintext));
            }
        }

        return envelopes;
    }

    private sealed class Devices : IPairedAppDevices
    {
        public void Remove(string deviceId)
        {
        }

        public IReadOnlyList<PairedAppDevice> All { get; } = [_phone, _other];

        public void Save(PairedAppDevice device) => throw new NotSupportedException();
    }

    private sealed class Counters : IBleCounters
    {
        private readonly Dictionary<string, ulong> _sent = [];
        private readonly Dictionary<string, ulong> _received = [];

        public ulong NextSend(string deviceId) => _sent[deviceId] = _sent.GetValueOrDefault(deviceId) + 1;

        public void Reset(string deviceId)
        {
            _sent.Remove(deviceId);
            _received.Remove(deviceId);
        }

        public bool TryAcceptReceived(string deviceId, ulong counter)
        {
            if (_received.TryGetValue(deviceId, out var last) && counter <= last)
            {
                return false;
            }

            _received[deviceId] = counter;
            return true;
        }
    }
}
