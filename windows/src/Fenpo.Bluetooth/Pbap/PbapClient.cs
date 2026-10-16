using System.Text;
using Fenpo.Bluetooth.Map;
using Fenpo.Bluetooth.Obex;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Fenpo.Bluetooth.Pbap;

/// <summary>
/// One session with the phone's Phone Book Server (PBAP PSE, 0x112F) over RFCOMM: pulls the main phone book.
/// </summary>
public sealed class PbapClient(ILogger<PbapClient> logger) : IAsyncDisposable
{
    private static readonly byte[] _target = Convert.FromHexString("796135F0F0C511D809660800200C9A66");

    // PBAP application parameters (PBAP 1.2, section 6.2.1).
    private const byte _maxListCount = 0x04;
    private const byte _propertySelector = 0x06;
    private const byte _format = 0x07;

    private readonly ILogger<PbapClient> _logger = logger;
    private StreamSocket? _socket;
    private ObexClient? _obex;

    /// <summary>
    /// Connects to the phone book server.
    /// </summary>
    /// <param name="device">The phone's classic Bluetooth device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="ObexResponse.Success"/>, the phone's refusal, or <see cref="ObexResponse.ServiceUnavailable"/>
    /// when the phone does not offer the service.</returns>
    public async Task<byte> ConnectAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
    {
        var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromShortId(0x112F), BluetoothCacheMode.Uncached).AsTask(cancellationToken);

        if (services.Services.FirstOrDefault() is not { } service)
        {
            _logger.LogInformation("No phone book service on {Device}: {Error}", device.Name, services.Error);
            return ObexResponse.ServiceUnavailable;
        }

        _socket = new StreamSocket();
        await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(cancellationToken);
        _obex = new ObexClient(_socket.InputStream.AsStreamForRead(0), _socket.OutputStream.AsStreamForWrite(0));
        var response = await _obex.ConnectAsync(_target, cancellationToken);
        return response.Code;
    }

    /// <summary>
    /// Pulls the main phone book with names and numbers only.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The vCards, or null when the phone refused.</returns>
    public async Task<string?> PullPhoneBookAsync(CancellationToken cancellationToken = default)
    {
        // vCard 3.0 (UTF-8, no quoted-printable); properties VERSION, FN, N and TEL only (bits 0, 1, 2, 7).
        var parameters = MapParameters.Build(
            (_format, MapParameters.U8(1)),
            (_propertySelector, [0, 0, 0, 0, 0, 0, 0, 0x87]),
            (_maxListCount, MapParameters.U16(ushort.MaxValue)));
        var obex = _obex ?? throw new InvalidOperationException("Phone book is not connected");
        var result = await obex.GetAsync([ObexHeader.Name("telecom/pb.vcf"), ObexHeader.Type("x-bt/phonebook"), new ObexHeader(ObexHeaderId.ApplicationParameters, parameters)], cancellationToken);

        if (result.Code != ObexResponse.Success)
        {
            _logger.LogWarning("Pulling the phone book failed: 0x{Code:X2}", result.Code);
            return null;
        }

        return Encoding.UTF8.GetString(result.Body);
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
}
