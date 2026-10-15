namespace Fenpo.Spikes.S2;

/// <summary>
/// Minimal OBEX client over a byte stream: Connect, SetPath, multi-packet Get and Put, Disconnect.
/// Every packet in both directions goes to the capture log.
/// </summary>
internal sealed class ObexClient(Stream input, Stream output, CaptureLog capture, string label)
{
    private const ushort _ourMaxPacket = 0xFFFE;

    private readonly Stream _input = input;
    private readonly Stream _output = output;
    private readonly CaptureLog _capture = capture;
    private readonly string _label = label;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _peerMaxPacket = 255;

    public uint? ConnectionId { get; private set; }

    public async Task<ObexPacket> ConnectAsync(byte[] target, byte[]? appParameters)
    {
        List<ObexHeader> headers = [ObexHeader.Target(target)];

        if (appParameters is not null)
        {
            headers.Add(ObexHeader.AppParameters(appParameters));
        }

        byte[] fields = [0x10, 0x00, _ourMaxPacket >> 8, _ourMaxPacket & 0xFF];
        var response = await ExchangeAsync(new ObexPacket(ObexCode.Connect, fields, headers), responseFieldLength: 4);

        if (response.Code == ObexCode.Success)
        {
            _peerMaxPacket = (response.Fields[2] << 8) | response.Fields[3];
            var id = response.Header(ObexHeaderId.ConnectionId);
            ConnectionId = id is null ? null : uint.Parse(id.AsText());
        }

        return response;
    }

    public int PeerMaxPacket => _peerMaxPacket;

    public Task<ObexPacket> SetPathAsync(string name, bool up = false)
    {
        // Flags: bit 0 = back up one level, bit 1 = do not create.
        byte[] fields = [(byte)(0x02 | (up ? 0x01 : 0x00)), 0x00];
        var headers = WithConnection();

        if (!up)
        {
            headers.Add(ObexHeader.Name(name));
        }

        return ExchangeAsync(new ObexPacket(ObexCode.SetPath, fields, headers));
    }

    public async Task<GetResult> GetAsync(params ObexHeader[] headers)
    {
        await _gate.WaitAsync();

        try
        {
            var body = new List<byte>();
            var responseHeaders = new List<ObexHeader>();
            var request = new ObexPacket(ObexCode.GetFinal, [], [.. WithConnection(), .. headers]);

            while (true)
            {
                var response = await ExchangeUnlockedAsync(request, 0);

                foreach (var header in response.Headers)
                {
                    if (header.Id is ObexHeaderId.Body or ObexHeaderId.EndOfBody)
                    {
                        body.AddRange(header.Value);
                    }
                    else
                    {
                        responseHeaders.Add(header);
                    }
                }

                if (response.Code != ObexCode.Continue)
                {
                    return new GetResult(response.Code, [.. body], responseHeaders);
                }

                request = new ObexPacket(ObexCode.GetFinal, [], WithConnection());
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ObexPacket> PutAsync(byte[] body, params ObexHeader[] headers)
    {
        await _gate.WaitAsync();

        try
        {
            var first = new List<ObexHeader>([.. WithConnection(), .. headers]);
            var room = _peerMaxPacket - 3 - first.Sum(h => h.EncodedLength) - 3;
            var offset = 0;
            ObexPacket response;

            while (true)
            {
                var chunk = Math.Min(room, body.Length - offset);
                var last = offset + chunk == body.Length;
                first.Add(ObexHeader.Body(body[offset..(offset + chunk)], last));
                offset += chunk;

                response = await ExchangeUnlockedAsync(new ObexPacket(last ? ObexCode.PutFinal : ObexCode.Put, [], first), 0);

                if (last || response.Code != ObexCode.Continue)
                {
                    return response;
                }

                first = WithConnection();
                room = _peerMaxPacket - 3 - first.Sum(h => h.EncodedLength) - 3;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ObexPacket> DisconnectAsync() => ExchangeAsync(new ObexPacket(ObexCode.Disconnect, [], WithConnection()));

    private List<ObexHeader> WithConnection() => ConnectionId is { } id ? [ObexHeader.ConnectionId(id)] : [];

    private async Task<ObexPacket> ExchangeAsync(ObexPacket request, int responseFieldLength = 0)
    {
        await _gate.WaitAsync();

        try
        {
            return await ExchangeUnlockedAsync(request, responseFieldLength);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ObexPacket> ExchangeUnlockedAsync(ObexPacket request, int responseFieldLength)
    {
        var encoded = request.Encode();
        _capture.Write("tx", _label, encoded, ObexCode.Name(request.Code));
        await _output.WriteAsync(encoded);
        await _output.FlushAsync();

        var raw = await ObexPacket.ReadAsync(_input);
        var response = ObexPacket.Decode(raw, responseFieldLength);
        _capture.Write("rx", _label, raw, ObexCode.Name(response.Code));
        return response;
    }
}

/// <summary>
/// Result of a (possibly multi-packet) GET.
/// </summary>
internal sealed record GetResult(byte Code, byte[] Body, List<ObexHeader> Headers)
{
    public byte[]? AppParameters => Headers.FirstOrDefault(h => h.Id == ObexHeaderId.ApplicationParameters)?.Value;
}
