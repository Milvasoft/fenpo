using Fenpo.Bluetooth.Obex;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Fenpo.Bluetooth.Map;

/// <summary>
/// Message Notification Server (MAP MNS, 0x1133): after notification registration the phone connects here and PUTs
/// event reports. The iPhone connected within 260 ms in S2.
/// </summary>
public sealed class MnsServer(ILogger<MnsServer> logger) : IDisposable
{
    private static readonly byte[] _target = Convert.FromHexString("BB582B41420C11DBB0DE0800200C9A66");
    private const uint _connectionId = 1;

    private readonly ILogger<MnsServer> _logger = logger;
    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;

    /// <summary>
    /// Raised from a background thread for every event the phone reports.
    /// </summary>
    public event EventHandler<MapEvent>? EventReceived;

    /// <summary>
    /// Gets a value indicating whether the server is advertised.
    /// </summary>
    public bool IsRunning => _provider is not null;

    /// <summary>
    /// Registers the SDP record and starts listening.
    /// </summary>
    /// <returns>A task that completes when advertising.</returns>
    public async Task StartAsync()
    {
        if (_provider is not null)
        {
            return;
        }

        var provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromShortId(0x1133));
        _listener = new StreamSocketListener();
        _listener.ConnectionReceived += (sender, args) => _ = Task.Run(() => ServeAsync(args.Socket));
        await _listener.BindServiceNameAsync(provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

        // ServiceName "MNS", profile MAP 1.4, MapSupportedFeatures (S2 record).
        SetAttribute(provider, 0x0100, [0x25, 0x03, .. "MNS"u8]);
        SetAttribute(provider, 0x0009, [0x35, 0x08, 0x35, 0x06, 0x19, 0x11, 0x34, 0x09, 0x01, 0x04]);
        SetAttribute(provider, 0x0317, [0x0A, 0x00, 0x00, 0x00, 0x7F]);

        provider.StartAdvertising(_listener);
        _provider = provider;
        _logger.LogInformation("Message notification server advertised");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _provider?.StopAdvertising();
        _provider = null;
        _listener?.Dispose();
        _listener = null;
    }

    private void SetAttribute(RfcommServiceProvider provider, uint id, byte[] value)
    {
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(value);
            provider.SdpRawAttributes[id] = writer.DetachBuffer();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "SDP attribute 0x{Id:X4} rejected", id);
        }
    }

    private async Task ServeAsync(StreamSocket socket)
    {
        var input = socket.InputStream.AsStreamForRead(0);
        var output = socket.OutputStream.AsStreamForWrite(0);
        var body = new List<byte>();

        try
        {
            while (true)
            {
                var raw = await ObexPacket.ReadAsync(input);
                var request = ObexPacket.Decode(raw, raw[0] == ObexOpcode.Connect ? 4 : 0);
                ObexPacket response;

                switch (request.Code)
                {
                    case ObexOpcode.Connect:
                        var accepted = request.Header(ObexHeaderId.Target)?.Value.AsSpan().SequenceEqual(_target) == true;
                        response = accepted
                            ? new ObexPacket(ObexResponse.Success, [0x10, 0x00, 0xFF, 0xFE], [ObexHeader.ConnectionId(_connectionId), new ObexHeader(ObexHeaderId.Who, _target)])
                            : new ObexPacket(ObexResponse.NotAcceptable, [0x10, 0x00, 0xFF, 0xFE], []);
                        break;
                    case ObexOpcode.Put:
                        body.AddRange(request.BodyBytes());
                        response = new ObexPacket(ObexResponse.Continue, [], []);
                        break;
                    case ObexOpcode.PutFinal:
                        body.AddRange(request.BodyBytes());
                        await SendAsync(output, new ObexPacket(ObexResponse.Success, [], []));
                        Report([.. body]);
                        body.Clear();
                        continue;
                    case ObexOpcode.Disconnect:
                        await SendAsync(output, new ObexPacket(ObexResponse.Success, [], []));
                        return;
                    default:
                        response = new ObexPacket(ObexResponse.NotImplemented, [], []);
                        break;
                }

                await SendAsync(output, response);
            }
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ObjectDisposedException or InvalidDataException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogDebug("Message notification connection closed: {Reason}", exception.GetType().Name);
        }
        finally
        {
            socket.Dispose();
        }
    }

    private void Report(byte[] body)
    {
        try
        {
            foreach (var mapEvent in MapEvent.Parse(body))
            {
                _logger.LogInformation("Message event {Type} in {Folder}", mapEvent.Type, mapEvent.Folder);
                EventReceived?.Invoke(this, mapEvent);
            }
        }
        catch (System.Xml.XmlException exception)
        {
            _logger.LogWarning(exception, "Unreadable message event report ({Length} bytes)", body.Length);
        }
    }

    private static async Task SendAsync(Stream output, ObexPacket packet)
    {
        await output.WriteAsync(packet.Encode());
        await output.FlushAsync();
    }
}
