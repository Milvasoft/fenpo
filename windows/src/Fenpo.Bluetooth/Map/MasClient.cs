using System.Text;
using Fenpo.Bluetooth.Obex;
using Fenpo.Core.Messages;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Fenpo.Bluetooth.Map;

/// <summary>
/// One session with the phone's Message Access Server (MAP MAS, 0x1132) over RFCOMM.
/// </summary>
public sealed class MasClient(ILogger<MasClient> logger) : IAsyncDisposable
{
    private static readonly byte[] _target = Convert.FromHexString("BB582B40420C11DBB0DE0800200C9A66");

    private readonly ILogger<MasClient> _logger = logger;
    private StreamSocket? _socket;
    private ObexClient? _obex;
    private string _folderPrefix = string.Empty;

    /// <summary>
    /// Connects and moves to telecom/msg.
    /// </summary>
    /// <param name="device">The phone's classic Bluetooth device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="ObexResponse.Success"/>, the phone's refusal (Forbidden when message access is off), or
    /// <see cref="ObexResponse.ServiceUnavailable"/> when the phone has no MAS.</returns>
    public async Task<byte> ConnectAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromShortId(0x1132), BluetoothCacheMode.Uncached).AsTask(cancellationToken);

        if (services.Services.FirstOrDefault() is not { } service)
        {
            _logger.LogInformation("No message access service on {Device}: {Error}", device.Name, services.Error);
            return ObexResponse.ServiceUnavailable;
        }

        _socket = new StreamSocket();
        await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(cancellationToken);
        _obex = new ObexClient(_socket.InputStream.AsStreamForRead(0), _socket.OutputStream.AsStreamForWrite(0));

        var response = await _obex.ConnectAsync(_target, cancellationToken);

        if (response.Code != ObexResponse.Success)
        {
            return response.Code;
        }

        // iOS 18 accepts SetPath (S2); older phones may not, then full paths go in the Name headers.
        var telecom = await _obex.SetPathAsync("telecom", cancellationToken: cancellationToken);
        var msg = telecom.Code == ObexResponse.Success ? await _obex.SetPathAsync("msg", cancellationToken: cancellationToken) : telecom;
        _folderPrefix = msg.Code == ObexResponse.Success ? string.Empty : "telecom/msg/";
        _logger.LogInformation("Message access connected (max packet {MaxPacket}, folders {Mode})", _obex.PeerMaxPacket, _folderPrefix.Length == 0 ? "by SetPath" : "by path");
        return ObexResponse.Success;
    }

    /// <summary>
    /// Lists the newest messages of a folder.
    /// </summary>
    /// <param name="folder">Folder.</param>
    /// <param name="maxCount">Maximum count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Messages, newest first.</returns>
    public async Task<IReadOnlyList<PhoneMessage>> ListAsync(MessageFolder folder, int maxCount, CancellationToken cancellationToken = default)
    {
        var parameters = MapParameters.Build(
            (MapParameters.MaxListCount, MapParameters.U16((ushort)Math.Clamp(maxCount, 1, ushort.MaxValue))),
            (MapParameters.SubjectLength, MapParameters.U8(255)));
        var result = await Obex.GetAsync([ObexHeader.Type("x-bt/MAP-msg-listing"), ObexHeader.Name(_folderPrefix + FolderName(folder)), new ObexHeader(ObexHeaderId.ApplicationParameters, parameters)], cancellationToken);

        if (result.Code != ObexResponse.Success)
        {
            _logger.LogWarning("Listing {Folder} failed: 0x{Code:X2}", folder, result.Code);
            return [];
        }

        return MapListing.Parse(result.Body, folder);
    }

    /// <summary>
    /// Gets a whole message.
    /// </summary>
    /// <param name="handle">Message handle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message, or null.</returns>
    public async Task<MessageContent?> GetAsync(string handle, CancellationToken cancellationToken = default)
    {
        var parameters = MapParameters.Build((MapParameters.Attachment, MapParameters.U8(0)), (MapParameters.Charset, MapParameters.U8(1)));
        var result = await Obex.GetAsync([ObexHeader.Type("x-bt/message"), ObexHeader.Name(handle), new ObexHeader(ObexHeaderId.ApplicationParameters, parameters)], cancellationToken);

        if (result.Code != ObexResponse.Success)
        {
            _logger.LogWarning("Getting message {Handle} failed: 0x{Code:X2}", handle, result.Code);
            return null;
        }

        return BMessage.Parse(Encoding.UTF8.GetString(result.Body));
    }

    /// <summary>
    /// Sets the read status of a message.
    /// </summary>
    /// <param name="handle">Message handle.</param>
    /// <param name="read">True for read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on success.</returns>
    public async Task<bool> SetReadAsync(string handle, bool read, CancellationToken cancellationToken = default)
    {
        var parameters = MapParameters.Build((MapParameters.StatusIndicator, MapParameters.U8(0)), (MapParameters.StatusValue, MapParameters.U8(read ? (byte)1 : (byte)0)));

        // The body is a single filler byte "0" (MAP 1.4, section 5.7).
        var response = await Obex.PutAsync([0x30], [ObexHeader.Type("x-bt/messageStatus"), ObexHeader.Name(handle), new ObexHeader(ObexHeaderId.ApplicationParameters, parameters)], cancellationToken);
        return response.Code == ObexResponse.Success;
    }

    /// <summary>
    /// Turns message event reports to our notification server on or off.
    /// </summary>
    /// <param name="on">True to receive events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on success.</returns>
    public async Task<bool> SetNotificationsAsync(bool on, CancellationToken cancellationToken = default)
    {
        var parameters = MapParameters.Build((MapParameters.NotificationStatus, MapParameters.U8(on ? (byte)1 : (byte)0)));
        var response = await Obex.PutAsync([0x30], [ObexHeader.Type("x-bt/MAP-NotificationRegistration"), new ObexHeader(ObexHeaderId.ApplicationParameters, parameters)], cancellationToken);
        return response.Code == ObexResponse.Success;
    }

    /// <summary>
    /// Puts an SMS into the outbox; the phone sends it.
    /// </summary>
    /// <param name="number">Recipient number.</param>
    /// <param name="text">Text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new message handle, or null when the phone refused.</returns>
    public async Task<string?> PushAsync(string number, string text, CancellationToken cancellationToken = default)
    {
        var parameters = MapParameters.Build((MapParameters.Charset, MapParameters.U8(1)), (MapParameters.Transparent, MapParameters.U8(0)), (MapParameters.Retry, MapParameters.U8(1)));
        var body = Encoding.UTF8.GetBytes(BMessage.BuildSms(number, text));
        var response = await Obex.PutAsync(body, [ObexHeader.Type("x-bt/message"), ObexHeader.Name(_folderPrefix + "outbox"), new ObexHeader(ObexHeaderId.ApplicationParameters, parameters)], cancellationToken);

        if (response.Code != ObexResponse.Success)
        {
            _logger.LogWarning("Sending failed: 0x{Code:X2}", response.Code);
            return null;
        }

        return response.Header(ObexHeaderId.Name)?.AsText() ?? string.Empty;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_obex is not null && _socket is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _obex.DisconnectAsync(timeout.Token);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
            {
                // The link is already gone.
            }
        }

        _socket?.Dispose();
        _socket = null;
        _obex = null;
    }

    private ObexClient Obex => _obex ?? throw new InvalidOperationException("Message access is not connected");

    private static string FolderName(MessageFolder folder) => folder switch
    {
        MessageFolder.Sent => "sent",
        MessageFolder.Outbox => "outbox",
        _ => "inbox",
    };
}
