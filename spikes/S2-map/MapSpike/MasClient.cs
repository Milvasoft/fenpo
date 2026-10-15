using System.Text;
using System.Xml.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Fenpo.Spikes.S2;

/// <summary>
/// MAP client (MCE) talking to the iPhone's Message Access Server over RFCOMM.
/// </summary>
internal sealed class MasClient(CaptureLog capture) : IDisposable
{
    public static readonly byte[] MasTarget = Convert.FromHexString("BB582B40420C11DBB0DE0800200C9A66");

    private readonly CaptureLog _capture = capture;
    private StreamSocket? _socket;
    private ObexClient? _obex;

    /// <summary>
    /// Path prefix for Name headers when SetPath is not supported.
    /// </summary>
    public string FolderPrefix { get; private set; } = string.Empty;

    public static async Task<RfcommDeviceService?> FindServiceAsync(BluetoothDevice device)
    {
        var result = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromShortId(0x1132), BluetoothCacheMode.Uncached);
        Log.Info("sdp", $"MAS lookup: {result.Error}, {result.Services.Count} instance(s)");

        foreach (var service in result.Services)
        {
            Log.Info("sdp", $"MAS {service.ConnectionHostName} channel {service.ConnectionServiceName}, protection {service.ProtectionLevel}");
            var attributes = await service.GetSdpRawAttributesAsync(BluetoothCacheMode.Uncached);

            foreach (var (id, buffer) in attributes.OrderBy(a => a.Key))
            {
                var bytes = new byte[buffer.Length];
                DataReader.FromBuffer(buffer).ReadBytes(bytes);
                Log.Info("sdp", $"  0x{id:X4} {SdpName(id)} = {Convert.ToHexString(bytes)}");
            }
        }

        return result.Services.FirstOrDefault();
    }

    public async Task<bool> ConnectAsync(RfcommDeviceService service)
    {
        _socket = new StreamSocket();
        await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
        _obex = new ObexClient(_socket.InputStream.AsStreamForRead(0), _socket.OutputStream.AsStreamForWrite(0), _capture, "mas");

        var response = await _obex.ConnectAsync(MasTarget, null);
        Log.Info("mas", $"Connect: {ObexCode.Name(response.Code)}, connection id {_obex.ConnectionId}, peer max packet {_obex.PeerMaxPacket}");

        if (response.Code != ObexCode.Success)
        {
            return false;
        }

        // Navigate to telecom/msg. If the iPhone refuses SetPath, use full paths in Name headers instead.
        var telecom = await _obex.SetPathAsync("telecom");
        var msg = telecom.Code == ObexCode.Success ? await _obex.SetPathAsync("msg") : telecom;
        Log.Info("mas", $"SetPath telecom: {ObexCode.Name(telecom.Code)}, msg: {ObexCode.Name(msg.Code)}");

        if (msg.Code != ObexCode.Success)
        {
            FolderPrefix = "telecom/msg/";
        }

        return true;
    }

    public async Task ListFoldersAsync()
    {
        var result = await Obex.GetAsync(ObexHeader.Type("x-obex/folder-listing"));
        Log.Info("mas", $"Folder listing: {ObexCode.Name(result.Code)}");
        Log.Info("mas", Encoding.UTF8.GetString(result.Body).ReplaceLineEndings(" "));
    }

    public async Task<List<MessageEntry>> ListMessagesAsync(string folder, ushort count)
    {
        var parameters = AppParams.Build(
            (AppParams.MaxListCount, AppParams.U16(count)),
            (AppParams.SubjectLength, AppParams.U8(64)));
        var result = await Obex.GetAsync(ObexHeader.Type("x-bt/MAP-msg-listing"), ObexHeader.Name(FolderPrefix + folder), ObexHeader.AppParameters(parameters));
        Log.Info("mas", $"Messages listing {folder}: {ObexCode.Name(result.Code)}, {result.Body.Length} bytes, params {Describe(result.AppParameters)}");

        if (result.Code != ObexCode.Success || result.Body.Length == 0)
        {
            return [];
        }

        var document = XDocument.Parse(Encoding.UTF8.GetString(result.Body).TrimEnd('\0'));
        var entries = document.Root!.Elements("msg").Select(MessageEntry.From).ToList();

        foreach (var entry in entries)
        {
            Log.Info("mas", $"  {entry}");
        }

        return entries;
    }

    public async Task<string?> GetMessageAsync(string handle)
    {
        var parameters = AppParams.Build(
            (AppParams.Attachment, AppParams.U8(0)),
            (AppParams.Charset, AppParams.U8(1)));
        var result = await Obex.GetAsync(ObexHeader.Type("x-bt/message"), ObexHeader.Name(handle), ObexHeader.AppParameters(parameters));
        Log.Info("mas", $"Get message {handle}: {ObexCode.Name(result.Code)}, {result.Body.Length} bytes");
        return result.Code == ObexCode.Success ? Encoding.UTF8.GetString(result.Body) : null;
    }

    public async Task SetReadAsync(string handle, bool read)
    {
        var parameters = AppParams.Build(
            (AppParams.StatusIndicator, AppParams.U8(0)),
            (AppParams.StatusValue, AppParams.U8(read ? (byte)1 : (byte)0)));
        var response = await Obex.PutAsync([0x30], ObexHeader.Type("x-bt/messageStatus"), ObexHeader.Name(handle), ObexHeader.AppParameters(parameters));
        Log.Info("mas", $"Set read={read} on {handle}: {ObexCode.Name(response.Code)}");
    }

    public async Task SetNotificationsAsync(bool on)
    {
        var parameters = AppParams.Build((AppParams.NotificationStatus, AppParams.U8(on ? (byte)1 : (byte)0)));
        var response = await Obex.PutAsync([0x30], ObexHeader.Type("x-bt/MAP-NotificationRegistration"), ObexHeader.AppParameters(parameters));
        Log.Info("mas", $"Notification registration {(on ? "on" : "off")}: {ObexCode.Name(response.Code)}");
    }

    public async Task PushMessageAsync(string number, string text)
    {
        var message = BMessage.BuildOutgoing(number, text);
        var parameters = AppParams.Build(
            (AppParams.Charset, AppParams.U8(1)),
            (AppParams.Transparent, AppParams.U8(0)),
            (AppParams.Retry, AppParams.U8(1)));
        var response = await Obex.PutAsync(Encoding.UTF8.GetBytes(message), ObexHeader.Type("x-bt/message"), ObexHeader.Name(FolderPrefix + "outbox"), ObexHeader.AppParameters(parameters));
        var handle = response.Header(ObexHeaderId.Name)?.AsText();
        Log.Info("mas", $"Push message: {ObexCode.Name(response.Code)}, handle {handle ?? "-"}");
    }

    public async Task DisconnectAsync()
    {
        if (_obex is not null)
        {
            var response = await _obex.DisconnectAsync();
            Log.Info("mas", $"Disconnect: {ObexCode.Name(response.Code)}");
        }
    }

    public void Dispose() => _socket?.Dispose();

    private ObexClient Obex => _obex ?? throw new InvalidOperationException("Not connected");

    private static string Describe(byte[]? parameters) => parameters is null ? "-" : AppParams.Describe(parameters);

    private static string SdpName(uint id) => id switch
    {
        0x0000 => "ServiceRecordHandle",
        0x0001 => "ServiceClassIdList",
        0x0004 => "ProtocolDescriptorList",
        0x0005 => "BrowseGroupList",
        0x0009 => "ProfileDescriptorList",
        0x0100 => "ServiceName",
        0x0200 => "GoepL2capPsm",
        0x0315 => "MasInstanceId",
        0x0316 => "SupportedMessageTypes",
        0x0317 => "MapSupportedFeatures",
        _ => string.Empty,
    };
}

/// <summary>
/// One entry of x-bt/MAP-msg-listing.
/// </summary>
internal sealed record MessageEntry(string Handle, string DateTime, string Sender, string SenderAddress, string Recipient, string Type, string Read, string Subject, string Size)
{
    public static MessageEntry From(XElement element) => new(
        Value(element, "handle"),
        Value(element, "datetime"),
        Value(element, "sender_name"),
        Value(element, "sender_addressing"),
        Value(element, "recipient_addressing"),
        Value(element, "type"),
        Value(element, "read"),
        Value(element, "subject"),
        Value(element, "size"));

    public override string ToString() => $"{Handle} {DateTime} {Type} read={Read} from=\"{Sender}\" <{SenderAddress}> to=<{Recipient}> size={Size} subject=\"{Subject}\"";

    private static string Value(XElement element, string name) => element.Attribute(name)?.Value ?? string.Empty;
}
