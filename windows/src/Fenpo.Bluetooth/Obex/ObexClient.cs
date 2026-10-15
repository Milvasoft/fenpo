namespace Fenpo.Bluetooth.Obex;

/// <summary>
/// OBEX client over a byte stream: Connect, SetPath, multi-packet Get and Put, Disconnect. One operation at a time.
/// </summary>
public sealed class ObexClient(Stream input, Stream output)
{
    private const ushort _ourMaxPacket = 0xFFFE;

    private readonly Stream _input = input;
    private readonly Stream _output = output;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Raised for every packet sent (true) or received (false), for logging and recordings.
    /// </summary>
    public event Action<bool, byte[]>? PacketObserved;

    /// <summary>
    /// Gets the connection id the server gave, if any.
    /// </summary>
    public uint? ConnectionId { get; private set; }

    /// <summary>
    /// Gets the largest packet the server accepts.
    /// </summary>
    public int PeerMaxPacket { get; private set; } = 255;

    /// <summary>
    /// Connects to a service.
    /// </summary>
    /// <param name="target">Target UUID of the service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    public async Task<ObexPacket> ConnectAsync(byte[] target, CancellationToken cancellationToken = default)
    {
        // Fields: OBEX version 1.0, flags, our maximum packet length.
        byte[] fields = [0x10, 0x00, _ourMaxPacket >> 8, _ourMaxPacket & 0xFF];
        var response = await ExchangeAsync(new ObexPacket(ObexOpcode.Connect, fields, [new ObexHeader(ObexHeaderId.Target, target)]), 4, cancellationToken);

        if (response.Code == ObexResponse.Success)
        {
            PeerMaxPacket = (response.Fields[2] << 8) | response.Fields[3];
            ConnectionId = response.Header(ObexHeaderId.ConnectionId)?.AsUInt32();
        }

        return response;
    }

    /// <summary>
    /// Changes into a child folder, or up one level.
    /// </summary>
    /// <param name="name">Folder name; ignored when going up.</param>
    /// <param name="up">True to go to the parent folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    public Task<ObexPacket> SetPathAsync(string name, bool up = false, CancellationToken cancellationToken = default)
    {
        // Flags: bit 0 backs up a level, bit 1 means "do not create".
        byte[] fields = [(byte)(0x02 | (up ? 0x01 : 0x00)), 0x00];
        var headers = WithConnection();

        if (!up)
        {
            headers.Add(ObexHeader.Name(name));
        }

        return ExchangeAsync(new ObexPacket(ObexOpcode.SetPath, fields, headers), 0, cancellationToken);
    }

    /// <summary>
    /// Gets an object, following Continue responses until the server is done.
    /// </summary>
    /// <param name="headers">Request headers (Type, Name, application parameters).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Final response code, body and the non-body headers of all responses.</returns>
    public async Task<ObexGetResult> GetAsync(IEnumerable<ObexHeader> headers, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var body = new List<byte>();
            var responseHeaders = new List<ObexHeader>();
            var request = new ObexPacket(ObexOpcode.GetFinal, [], [.. WithConnection(), .. headers]);

            while (true)
            {
                var response = await ExchangeUnlockedAsync(request, 0, cancellationToken);
                body.AddRange(response.BodyBytes());
                responseHeaders.AddRange(response.Headers.Where(h => h.Id is not (ObexHeaderId.Body or ObexHeaderId.EndOfBody)));

                if (response.Code != ObexResponse.Continue)
                {
                    return new ObexGetResult(response.Code, [.. body], responseHeaders);
                }

                request = new ObexPacket(ObexOpcode.GetFinal, [], WithConnection());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Puts an object, split into packets the server accepts.
    /// </summary>
    /// <param name="body">Object bytes.</param>
    /// <param name="headers">Headers of the first packet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The last response.</returns>
    public async Task<ObexPacket> PutAsync(byte[] body, IEnumerable<ObexHeader> headers, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var packetHeaders = new List<ObexHeader>([.. WithConnection(), .. headers]);
            var offset = 0;

            while (true)
            {
                // Room left after the packet prefix, the other headers and the body header's own prefix.
                var room = Math.Max(0, PeerMaxPacket - 3 - packetHeaders.Sum(h => h.EncodedLength) - 3);
                var chunk = Math.Min(room, body.Length - offset);
                var last = offset + chunk == body.Length;
                packetHeaders.Add(new ObexHeader(last ? ObexHeaderId.EndOfBody : ObexHeaderId.Body, body[offset..(offset + chunk)]));
                offset += chunk;

                var response = await ExchangeUnlockedAsync(new ObexPacket(last ? ObexOpcode.PutFinal : ObexOpcode.Put, [], packetHeaders), 0, cancellationToken);

                if (last || response.Code != ObexResponse.Continue)
                {
                    return response;
                }

                packetHeaders = WithConnection();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Ends the session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    public Task<ObexPacket> DisconnectAsync(CancellationToken cancellationToken = default) => ExchangeAsync(new ObexPacket(ObexOpcode.Disconnect, [], WithConnection()), 0, cancellationToken);

    private List<ObexHeader> WithConnection() => ConnectionId is { } id ? [ObexHeader.ConnectionId(id)] : [];

    private async Task<ObexPacket> ExchangeAsync(ObexPacket request, int responseFieldLength, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await ExchangeUnlockedAsync(request, responseFieldLength, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ObexPacket> ExchangeUnlockedAsync(ObexPacket request, int responseFieldLength, CancellationToken cancellationToken)
    {
        var encoded = request.Encode();
        PacketObserved?.Invoke(true, encoded);
        await _output.WriteAsync(encoded, cancellationToken);
        await _output.FlushAsync(cancellationToken);

        var raw = await ObexPacket.ReadAsync(_input, cancellationToken);
        PacketObserved?.Invoke(false, raw);
        return ObexPacket.Decode(raw, responseFieldLength);
    }
}

/// <summary>
/// Result of a (possibly multi-packet) GET.
/// </summary>
/// <param name="Code">Final response code.</param>
/// <param name="Body">Body of all packets.</param>
/// <param name="Headers">Other headers of all packets.</param>
public sealed record ObexGetResult(byte Code, byte[] Body, IReadOnlyList<ObexHeader> Headers)
{
    /// <summary>
    /// Gets the application parameters header payload, if the server sent one.
    /// </summary>
    public byte[]? ApplicationParameters => Headers.FirstOrDefault(h => h.Id == ObexHeaderId.ApplicationParameters)?.Value;
}
