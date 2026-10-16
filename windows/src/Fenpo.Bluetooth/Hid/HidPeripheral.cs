using System.Runtime.InteropServices.WindowsRuntime;
using Fenpo.Core.Input;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Fenpo.Bluetooth.Hid;

/// <summary>
/// The PC as a Bluetooth keyboard and mouse (HID over GATT, research §7) for the paired phone: the HID service with a
/// keyboard and a mouse input report, and a battery service, published only while the phone is being controlled so the
/// phone's own on-screen keyboard is back the rest of the time. iOS shows the pointer with AssistiveTouch on.
/// </summary>
public sealed class HidPeripheral(ILogger<HidPeripheral> logger) : IAsyncDisposable
{
    private static readonly Guid _hidService = BluetoothUuidHelper.FromShortId(0x1812);
    private static readonly Guid _batteryService = BluetoothUuidHelper.FromShortId(0x180F);
    private static readonly Guid _hidInformation = BluetoothUuidHelper.FromShortId(0x2A4A);
    private static readonly Guid _reportMap = BluetoothUuidHelper.FromShortId(0x2A4B);
    private static readonly Guid _controlPoint = BluetoothUuidHelper.FromShortId(0x2A4C);
    private static readonly Guid _report = BluetoothUuidHelper.FromShortId(0x2A4D);
    private static readonly Guid _protocolMode = BluetoothUuidHelper.FromShortId(0x2A4E);
    private static readonly Guid _reportReference = BluetoothUuidHelper.FromShortId(0x2908);
    private static readonly Guid _batteryLevel = BluetoothUuidHelper.FromShortId(0x2A19);

    private readonly ILogger<HidPeripheral> _logger = logger;
    private readonly SemaphoreSlim _sending = new(1, 1);
    private GattServiceProvider? _hid;
    private GattServiceProvider? _battery;
    private GattLocalCharacteristic? _keyboard;
    private GattLocalCharacteristic? _mouse;

    /// <summary>
    /// Raised when a device subscribes to the reports or goes away.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value indicating whether the service is published.
    /// </summary>
    public bool IsRunning => _hid is not null;

    /// <summary>
    /// Gets a value indicating whether a device (the phone) takes the reports.
    /// </summary>
    public bool Connected => _keyboard?.SubscribedClients.Count > 0 || _mouse?.SubscribedClients.Count > 0;

    /// <summary>
    /// Publishes the keyboard and mouse.
    /// </summary>
    /// <returns>False when the adapter cannot do it.</returns>
    public async Task<bool> StartAsync()
    {
        if (_hid is not null)
        {
            return true;
        }

        try
        {
            var created = await GattServiceProvider.CreateAsync(_hidService);

            if (created.Error != BluetoothError.Success)
            {
                _logger.LogWarning("HID service could not be created: {Error}", created.Error);
                return false;
            }

            var service = created.ServiceProvider.Service;

            // bcdHID 1.11, no country, normally connectable.
            await Constant(service, _hidInformation, [0x11, 0x01, 0x00, 0x02]);
            await Constant(service, _reportMap, HidReports.ReportMap.ToArray());

            var protocol = await service.CreateCharacteristicAsync(_protocolMode, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.WriteWithoutResponse,
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            protocol.Characteristic.ReadRequested += async (sender, args) => await AnswerAsync(args, [0x01]);
            protocol.Characteristic.WriteRequested += async (sender, args) => await AcceptAsync(args);

            var control = await service.CreateCharacteristicAsync(_controlPoint, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            control.Characteristic.WriteRequested += async (sender, args) => await AcceptAsync(args);

            _keyboard = await InputReport(service, HidReports.KeyboardId, new byte[8]);
            _mouse = await InputReport(service, HidReports.MouseId, new byte[4]);

            // The keyboard's LEDs (caps lock) the phone writes back.
            var leds = await service.CreateCharacteristicAsync(_report, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            await Reference(leds.Characteristic, HidReports.KeyboardId, output: true);
            leds.Characteristic.ReadRequested += async (sender, args) => await AnswerAsync(args, [0x00]);
            leds.Characteristic.WriteRequested += async (sender, args) => await AcceptAsync(args);

            created.ServiceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
            _hid = created.ServiceProvider;

            var battery = await GattServiceProvider.CreateAsync(_batteryService);

            if (battery.Error == BluetoothError.Success)
            {
                var level = await battery.ServiceProvider.Service.CreateCharacteristicAsync(_batteryLevel, new GattLocalCharacteristicParameters
                {
                    CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
                    ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                });
                level.Characteristic.ReadRequested += async (sender, args) => await AnswerAsync(args, [100]);
                battery.ServiceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = false });
                _battery = battery.ServiceProvider;
            }

            _logger.LogInformation("Keyboard and mouse published for the phone");
            return true;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Publishing the keyboard and mouse failed");
            await StopAsync();
            return false;
        }
    }

    /// <summary>
    /// Takes the keyboard and mouse away; the phone's on-screen keyboard comes back.
    /// </summary>
    /// <returns>A task that completes when stopped.</returns>
    public Task StopAsync()
    {
        _hid?.StopAdvertising();
        _battery?.StopAdvertising();
        _hid = null;
        _battery = null;
        _keyboard = null;
        _mouse = null;
        Changed?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Keyboard and mouse taken away from the phone");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a keyboard report.
    /// </summary>
    /// <param name="report">Eight bytes: modifiers, reserved, six keys.</param>
    /// <returns>A task that completes when sent.</returns>
    public Task SendKeyboardAsync(byte[] report) => NotifyAsync(_keyboard, report);

    /// <summary>
    /// Sends a mouse report.
    /// </summary>
    /// <param name="report">Four bytes: buttons, x, y, wheel.</param>
    /// <returns>A task that completes when sent.</returns>
    public Task SendMouseAsync(byte[] report) => NotifyAsync(_mouse, report);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task NotifyAsync(GattLocalCharacteristic? characteristic, byte[] report)
    {
        if (characteristic is null || characteristic.SubscribedClients.Count == 0)
        {
            return;
        }

        // Reports go out in order: a key release must not overtake its press.
        await _sending.WaitAsync();

        try
        {
            await characteristic.NotifyValueAsync(report.AsBuffer());
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            _logger.LogDebug("Sending a HID report failed: {Reason}", exception.Message);
        }
        finally
        {
            _sending.Release();
        }
    }

    private static async Task Constant(GattLocalService service, Guid uuid, byte[] value)
    {
        var created = await service.CreateCharacteristicAsync(uuid, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = value.AsBuffer(),
        });

        if (created.Error != BluetoothError.Success)
        {
            throw new UnauthorizedAccessException($"Characteristic {uuid} could not be created: {created.Error}");
        }
    }

    private async Task<GattLocalCharacteristic> InputReport(GattLocalService service, byte id, byte[] empty)
    {
        var created = await service.CreateCharacteristicAsync(_report, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
        });

        if (created.Error != BluetoothError.Success)
        {
            throw new UnauthorizedAccessException($"Report {id} could not be created: {created.Error}");
        }

        await Reference(created.Characteristic, id, output: false);
        created.Characteristic.ReadRequested += async (sender, args) => await AnswerAsync(args, empty);
        created.Characteristic.SubscribedClientsChanged += (sender, args) =>
        {
            _logger.LogInformation("Report {Id} subscribers: {Count}", id, sender.SubscribedClients.Count);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        return created.Characteristic;
    }

    // Report Reference: which report id this characteristic is and whether it is input (1) or output (2).
    private static async Task Reference(GattLocalCharacteristic characteristic, byte id, bool output)
    {
        await characteristic.CreateDescriptorAsync(_reportReference, new GattLocalDescriptorParameters
        {
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = new byte[] { id, output ? (byte)2 : (byte)1 }.AsBuffer(),
        });
    }

    private static async Task AnswerAsync(GattReadRequestedEventArgs args, byte[] value)
    {
        using var deferral = args.GetDeferral();
        var request = await args.GetRequestAsync();
        request?.RespondWithValue(value.AsBuffer());
    }

    private static async Task AcceptAsync(GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        var request = await args.GetRequestAsync();

        if (request?.Option == GattWriteOption.WriteWithResponse)
        {
            request.Respond();
        }
    }
}
