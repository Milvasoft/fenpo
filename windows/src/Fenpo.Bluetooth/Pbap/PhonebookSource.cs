using Fenpo.Bluetooth.Link;
using Fenpo.Bluetooth.Obex;
using Fenpo.Core.Contacts;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;

namespace Fenpo.Bluetooth.Pbap;

/// <summary>
/// The iPhone's contacts over PBAP, read once per connection so messages and calls show names instead of numbers.
/// Nothing is written to disk. One try per connection: the iPhone in our tests refused the RFCOMM connection outright
/// and never offered "Sync Contacts", probably because it only syncs contacts with hands-free (HFP) devices.
/// </summary>
public sealed class PhonebookSource(IphoneLink link, PbapClientRecord record, ILogger<PbapClient> pbapLogger, ILogger<PhonebookSource> logger) : IContactSource
{
    private readonly IphoneLink _link = link;
    private readonly PbapClientRecord _record = record;
    private readonly ILogger<PbapClient> _pbapLogger = pbapLogger;
    private readonly ILogger<PhonebookSource> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _classicDeviceId;
    private bool _readThisConnection;

    /// <inheritdoc/>
    public event EventHandler? ContactsChanged;

    /// <inheritdoc/>
    public ContactsAvailability Availability { get; private set; } = ContactsAvailability.Unavailable;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> NamesByNumber { get; private set; } = new Dictionary<string, string>();

    /// <summary>
    /// Starts following the link. Call once.
    /// </summary>
    public void Attach()
    {
        _link.ServicesReady += (sender, services) => _ = ReadAsync();
        _link.ConnectionChanged += (sender, connected) =>
        {
            if (!connected)
            {
                _readThisConnection = false;
            }
        };
    }

    /// <summary>
    /// Sets the phone's classic Bluetooth record (PBAP runs over classic RFCOMM).
    /// </summary>
    /// <param name="classicDeviceId">BluetoothDevice id, or null when the phone has none.</param>
    public void UsePhone(string? classicDeviceId)
    {
        _classicDeviceId = classicDeviceId;

        if (_link.IsConnected)
        {
            _ = ReadAsync();
        }
    }

    private async Task ReadAsync()
    {
        if (_classicDeviceId is null || _readThisConnection || !await _gate.WaitAsync(0))
        {
            return;
        }

        await _record.StartAsync();
        _readThisConnection = true;
        var client = new PbapClient(_pbapLogger);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            if (await BluetoothDevice.FromIdAsync(_classicDeviceId).AsTask(timeout.Token) is not { } device)
            {
                return;
            }

            var code = await ClassicConnectGate.RunAsync(() => client.ConnectAsync(device, timeout.Token), timeout.Token);

            if (code != ObexResponse.Success || await client.PullPhoneBookAsync(timeout.Token) is not { } vcards)
            {
                _logger.LogInformation("Phone book refused: 0x{Code:X2}", code);
                Availability = ContactsAvailability.NeedsPermission;
                return;
            }

            var contacts = VCard.Parse(vcards);
            NamesByNumber = VCard.NamesByNumber(contacts);
            Availability = ContactsAvailability.Ready;
            _logger.LogInformation("Phone book read: {Contacts} contacts, {Numbers} numbers", contacts.Count, NamesByNumber.Count);
            ContactsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            _logger.LogInformation("Reading the phone book failed: {Reason}", exception.Message.Trim());
        }
        finally
        {
            await client.DisposeAsync();
            _gate.Release();
        }
    }
}
