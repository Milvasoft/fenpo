using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Fenpo.Bluetooth.Pbap;

/// <summary>
/// Advertises this PC as a phone book client (PBAP PCE, 0x112E). Windows has no such record of its own, and the iPhone
/// offers "Sync Contacts" only to peers that advertise the client role, as it did for messages once the MNS record was
/// up (S2). Nothing connects to this record.
/// </summary>
public sealed class PbapClientRecord(ILogger<PbapClientRecord> logger) : IDisposable
{
    private readonly ILogger<PbapClientRecord> _logger = logger;
    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;

    /// <summary>
    /// Publishes the record. Safe to call more than once.
    /// </summary>
    /// <returns>A task that completes when the record is advertised.</returns>
    public async Task StartAsync()
    {
        if (_provider is not null)
        {
            return;
        }

        try
        {
            var provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromShortId(0x112E));
            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += (sender, args) =>
            {
                // Nothing is served here; a peer that connects anyway is closed. Reading Socket throws when the peer
                // already gave up (0x800703E3), and an exception escaping this WinRT event reaches the app.
                try
                {
                    args.Socket.Dispose();
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                }
            };
            await _listener.BindServiceNameAsync(provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            // ServiceName "Phonebook Access PCE", profile PBAP (0x1130) version 1.2.
            var writer = new DataWriter();
            writer.WriteBytes([0x25, 0x14, .. "Phonebook Access PCE"u8]);
            provider.SdpRawAttributes[0x0100] = writer.DetachBuffer();
            writer.WriteBytes([0x35, 0x08, 0x35, 0x06, 0x19, 0x11, 0x30, 0x09, 0x01, 0x02]);
            provider.SdpRawAttributes[0x0009] = writer.DetachBuffer();

            provider.StartAdvertising(_listener);
            _provider = provider;
            _logger.LogInformation("Phone book client record advertised");
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Phone book client record could not be advertised");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _provider?.StopAdvertising();
        _provider = null;
        _listener?.Dispose();
        _listener = null;
    }
}
