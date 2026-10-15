using System.Text;
using System.Xml.Linq;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Fenpo.Spikes.S2;

/// <summary>
/// Message Notification Server: the iPhone connects here after SetNotificationRegistration and PUTs
/// x-bt/MAP-event-report documents.
/// </summary>
internal sealed class MnsServer(CaptureLog capture, Func<MapEvent, Task> onEvent) : IDisposable
{
    private static readonly byte[] _mnsTarget = Convert.FromHexString("BB582B41420C11DBB0DE0800200C9A66");
    private const uint _connectionId = 1;

    private readonly CaptureLog _capture = capture;
    private readonly Func<MapEvent, Task> _onEvent = onEvent;
    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;

    public async Task StartAsync()
    {
        _provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromShortId(0x1133));
        _listener = new StreamSocketListener();
        _listener.ConnectionReceived += (sender, args) => Task.Run(() => ServeAsync(args.Socket));
        await _listener.BindServiceNameAsync(_provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

        // ServiceName "MNS", BluetoothProfileDescriptorList (MAP 0x1134, version 1.4), MapSupportedFeatures.
        SetAttribute(0x0100, [0x25, 0x03, .. "MNS"u8]);
        SetAttribute(0x0009, [0x35, 0x08, 0x35, 0x06, 0x19, 0x11, 0x34, 0x09, 0x01, 0x04]);
        SetAttribute(0x0317, [0x0A, 0x00, 0x00, 0x00, 0x7F]);

        _provider.StartAdvertising(_listener);
        Log.Info("mns", $"Listening on {_provider.ServiceId.AsString()}");
    }

    public void Dispose()
    {
        _provider?.StopAdvertising();
        _listener?.Dispose();
    }

    private void SetAttribute(uint id, byte[] value)
    {
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(value);
            _provider!.SdpRawAttributes[id] = writer.DetachBuffer();
        }
        catch (Exception exception)
        {
            Log.Warn("mns", $"SDP attribute 0x{id:X4} rejected: {exception.Message}");
        }
    }

    private async Task ServeAsync(StreamSocket socket)
    {
        Log.Info("mns", $"iPhone connected from {socket.Information.RemoteHostName}");
        var input = socket.InputStream.AsStreamForRead(0);
        var output = socket.OutputStream.AsStreamForWrite(0);
        var body = new List<byte>();

        try
        {
            while (true)
            {
                var raw = await ObexPacket.ReadAsync(input);
                var opcode = raw[0];
                var request = ObexPacket.Decode(raw, opcode == ObexCode.Connect ? 4 : 0);
                _capture.Write("rx", "mns", raw, ObexCode.Name(opcode));

                ObexPacket response;

                switch (opcode)
                {
                    case ObexCode.Connect:
                        var target = request.Header(ObexHeaderId.Target)?.Value;
                        var ok = target is not null && target.AsSpan().SequenceEqual(_mnsTarget);
                        response = ok
                            ? new ObexPacket(ObexCode.Success, [0x10, 0x00, 0xFF, 0xFE], [ObexHeader.ConnectionId(_connectionId), ObexHeader.Who(_mnsTarget)])
                            : new ObexPacket(0xC6, [0x10, 0x00, 0xFF, 0xFE], []);
                        Log.Info("mns", $"OBEX connect, target {(ok ? "MNS" : "unexpected")}");
                        break;
                    case ObexCode.Put:
                    case ObexCode.PutFinal:
                        foreach (var header in request.Headers.Where(h => h.Id is ObexHeaderId.Body or ObexHeaderId.EndOfBody))
                        {
                            body.AddRange(header.Value);
                        }

                        if (request.Header(ObexHeaderId.ApplicationParameters) is { } parameters)
                        {
                            Log.Info("mns", $"PUT params {AppParams.Describe(parameters.Value)}");
                        }

                        if (opcode == ObexCode.PutFinal)
                        {
                            response = new ObexPacket(ObexCode.Success, [], []);
                            var xml = Encoding.UTF8.GetString([.. body]).TrimEnd('\0');
                            body.Clear();
                            await SendAsync(output, response);
                            HandleEventReport(xml);
                            continue;
                        }

                        response = new ObexPacket(ObexCode.Continue, [], []);
                        break;
                    case ObexCode.Disconnect:
                        await SendAsync(output, new ObexPacket(ObexCode.Success, [], []));
                        Log.Info("mns", "OBEX disconnect");
                        return;
                    default:
                        response = new ObexPacket(0xD1, [], []);
                        break;
                }

                await SendAsync(output, response);
            }
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ObjectDisposedException)
        {
            Log.Info("mns", $"Connection closed: {exception.GetType().Name}");
        }
        finally
        {
            socket.Dispose();
        }
    }

    private async Task SendAsync(Stream output, ObexPacket packet)
    {
        var encoded = packet.Encode();
        _capture.Write("tx", "mns", encoded, ObexCode.Name(packet.Code));
        await output.WriteAsync(encoded);
        await output.FlushAsync();
    }

    private void HandleEventReport(string xml)
    {
        Log.Info("mns", $"Event report: {xml.ReplaceLineEndings(" ")}");

        try
        {
            foreach (var element in XDocument.Parse(xml).Root!.Elements("event"))
            {
                var mapEvent = new MapEvent(
                    element.Attribute("type")?.Value ?? string.Empty,
                    element.Attribute("handle")?.Value ?? string.Empty,
                    element.Attribute("folder")?.Value ?? string.Empty,
                    element.Attribute("msg_type")?.Value ?? string.Empty,
                    DateTimeOffset.Now);
                _ = _onEvent(mapEvent);
            }
        }
        catch (Exception exception)
        {
            Log.Warn("mns", $"Event report parse failed: {exception.Message}");
        }
    }
}

/// <summary>
/// One event from a MAP event report.
/// </summary>
internal sealed record MapEvent(string Type, string Handle, string Folder, string MessageType, DateTimeOffset ReceivedAt);
