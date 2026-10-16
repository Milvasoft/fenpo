using Fenpo.Protocol.V1;
using Google.Protobuf;

namespace Fenpo.Core.Channel;

/// <summary>
/// The PC end of one phone's Fenpo BLE connection: joins chunks, opens and checks sealed messages, answers Hello with
/// Hello and PcStatus and Ping with Pong, and seals and splits what the PC sends (protocol/README.md). The transport
/// (GATT server) feeds <see cref="OnChunk"/> and delivers <see cref="ChunkReady"/>.
/// </summary>
public sealed class BleChannelSession(IPairedAppDevices devices, IBleCounters counters, PcIdentity identity, Func<PcStatus> pcStatus, int payloadLength)
{
    private readonly IPairedAppDevices _devices = devices;
    private readonly IBleCounters _counters = counters;
    private readonly PcIdentity _identity = identity;
    private readonly Func<PcStatus> _pcStatus = pcStatus;
    private readonly BleChunkAssembler _assembler = new();
    private readonly Lock _sendLock = new();
    private PairedAppDevice? _device;
    private ulong _nextId = 1;

    /// <summary>
    /// Gets or sets the payload bytes per chunk; updated when the phone subscribes and its notification size is known.
    /// </summary>
    public int PayloadLength { get; set; } = payloadLength;

    /// <summary>
    /// Raised with every chunk to notify on <c>tx</c>, in order.
    /// </summary>
    public event EventHandler<byte[]>? ChunkReady;

    /// <summary>
    /// Raised for messages the session does not answer itself (clipboard, ring, transfers, ...).
    /// </summary>
    public event EventHandler<Envelope>? MessageReceived;

    /// <summary>
    /// Raised when a phone says Hello; the session is bound to it from then on.
    /// </summary>
    public event EventHandler<Hello>? HelloReceived;

    /// <summary>
    /// Gets the phone this connection belongs to, once a sealed message from it opened.
    /// </summary>
    public PairedAppDevice? Device => _device;

    /// <summary>
    /// Feeds one chunk written to <c>rx</c>.
    /// </summary>
    /// <param name="chunk">Chunk bytes.</param>
    /// <returns>The reason a completed message was dropped, or null.</returns>
    public string? OnChunk(ReadOnlySpan<byte> chunk)
    {
        var dropped = _assembler.Dropped;

        if (!_assembler.TryAdd(chunk, out var sealedMessage) || sealedMessage is null)
        {
            return _assembler.Dropped != dropped ? $"chunk {chunk.Length} bytes, flags {(chunk.Length > 0 ? chunk[0] : 0)}, out of order or too long" : null;
        }

        // A connection may belong to any paired phone until one key opens a message; then it stays with that phone.
        var candidates = _device is { } bound ? [bound] : _devices.All;
        var opened = candidates.Select(d => (Device: d, Ok: BleSeal.TryOpen(d.BleKey, sealedMessage, BleSender.Phone, out var plaintext, out var counter), Plaintext: plaintext, Counter: counter))
            .FirstOrDefault(r => r.Ok);

        if (opened.Device is null)
        {
            return "no paired phone's key opens the message";
        }

        if (!_counters.TryAcceptReceived(opened.Device.DeviceId, opened.Counter))
        {
            return "replayed or old counter";
        }

        _device = opened.Device;
        Envelope envelope;

        try
        {
            envelope = Envelope.Parser.ParseFrom(opened.Plaintext);
        }
        catch (InvalidProtocolBufferException)
        {
            return "not an Envelope";
        }

        Handle(envelope);
        return null;
    }

    /// <summary>
    /// Sends a message to the bound phone.
    /// </summary>
    /// <param name="envelope">Message; version and id are filled in.</param>
    /// <returns>False when no phone is bound yet.</returns>
    public bool Send(Envelope envelope)
    {
        if (_device is not { } device)
        {
            return false;
        }

        IReadOnlyList<byte[]> chunks;

        // Counter, sealing and chunk order must match send order.
        lock (_sendLock)
        {
            envelope.Version = 1;
            envelope.Id = _nextId++;
            var sealedMessage = BleSeal.Seal(device.BleKey, BleSender.Pc, _counters.NextSend(device.DeviceId), envelope.ToByteArray());
            chunks = BleChunks.Split(sealedMessage, PayloadLength);

            foreach (var chunk in chunks)
            {
                ChunkReady?.Invoke(this, chunk);
            }
        }

        return true;
    }

    /// <summary>
    /// Sends the current PC status (after Hello and whenever ANCS or MAP access changes).
    /// </summary>
    /// <returns>False when no phone is bound yet.</returns>
    public bool SendPcStatus() => Send(new Envelope { PcStatus = _pcStatus() });

    private void Handle(Envelope envelope)
    {
        switch (envelope.BodyCase)
        {
            case Envelope.BodyOneofCase.Hello:
                // The PC's Hello comes first; whatever listeners send after Hello follows it.
                Send(new Envelope { ReplyTo = envelope.Id, Hello = PcHello.Create(_identity) });
                SendPcStatus();
                HelloReceived?.Invoke(this, envelope.Hello);
                break;
            case Envelope.BodyOneofCase.Ping:
                Send(new Envelope { ReplyTo = envelope.Id, Pong = new Pong { Nonce = envelope.Ping.Nonce } });
                break;
            case Envelope.BodyOneofCase.Pong:
                break;
            default:
                MessageReceived?.Invoke(this, envelope);
                break;
        }
    }
}
