using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Fenpo.Bluetooth.AppChannel;
using Fenpo.Bluetooth.Status;
using Fenpo.Core.Channel;
using Fenpo.Core.Devices;
using Fenpo.Protocol.V1;
using Fenpo.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// A message from Fenpo on the phone and the channel it came on.
/// </summary>
/// <param name="Device">Phone.</param>
/// <param name="Envelope">Message.</param>
/// <param name="OverWifi">True for the Wi‑Fi control connection, false for the BLE channel.</param>
public sealed record ChannelMessage(PairedAppDevice Device, Envelope Envelope, bool OverWifi);

/// <summary>
/// Fenpo on the phone over both channels as one: the Wi‑Fi control connection (④) and the BLE channel (③). Messages go
/// to the phone in use (<see cref="IPhoneStatusSource.Active"/>), over Wi‑Fi while it is up and over Bluetooth
/// otherwise; what it offered in Hello decides which features are on. Messages from either phone are passed on. Keeps
/// each phone's status up to date: app and Wi‑Fi connected, the OS version, and for an Android phone (no Bluetooth
/// link of its own) the name, model and battery from DeviceState.
/// </summary>
public sealed class PhoneChannel(
    ControlServer wifi,
    FenpoGattServer ble,
    PhoneStatusTracker status,
    IPairedAppDevices devices,
    ILogger<PhoneChannel> logger) : IHostedService
{
    private readonly ControlServer _wifi = wifi;
    private readonly FenpoGattServer _ble = ble;
    private readonly PhoneStatusTracker _status = status;
    private readonly IPairedAppDevices _devices = devices;
    private readonly ILogger<PhoneChannel> _logger = logger;
    private readonly ConcurrentDictionary<string, Hello> _wifiHellos = new();
    private readonly ConcurrentDictionary<string, Hello> _bleHellos = new();
    private readonly ConcurrentDictionary<string, PairedAppDevice> _connected = new();

    /// <summary>
    /// Raised from a background thread for every message the channels do not answer themselves.
    /// </summary>
    public event EventHandler<ChannelMessage>? MessageReceived;

    /// <summary>
    /// Raised when the phone connects, leaves or a channel comes or goes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised after a phone's Hello on either channel (a notification snapshot follows on that channel).
    /// </summary>
    public event EventHandler<ChannelMessage>? HelloReceived;

    /// <summary>
    /// Gets the phone Fenpo talks to: the phone in use, when Fenpo on it is connected.
    /// </summary>
    public PairedAppDevice? Phone => _connected.Values.FirstOrDefault(d => KindOf(HelloOf(d.DeviceId)) == _status.Active);

    /// <summary>
    /// Gets a value indicating whether the phone is on either channel.
    /// </summary>
    public bool IsConnected => Phone is { } phone && (_wifiHellos.ContainsKey(phone.DeviceId) || _bleHellos.ContainsKey(phone.DeviceId));

    /// <summary>
    /// Gets a value indicating whether the phone is connected over Wi‑Fi (needed for files and large items).
    /// </summary>
    public bool WifiConnected => Phone is { } phone && _wifiHellos.ContainsKey(phone.DeviceId);

    /// <summary>
    /// Gets the phone's platform, or unspecified when it is not connected.
    /// </summary>
    public DevicePlatform Platform => Phone is { } phone && HelloOf(phone.DeviceId) is { } hello ? hello.Platform : DevicePlatform.Unspecified;

    /// <summary>
    /// Gets a value indicating whether the connected phone offered a feature in its Hello.
    /// </summary>
    /// <param name="capability">Feature.</param>
    /// <returns>True when both sides have it.</returns>
    public bool Supports(Capability capability) => Phone is { } phone && HelloOf(phone.DeviceId) is { } hello && hello.Capabilities.Contains(capability);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _wifi.HelloReceived += (sender, e) => OnHello(e.Device, e.Hello, overWifi: true);
        _wifi.SessionChanged += (sender, e) =>
        {
            if (!e.Connected)
            {
                OnLeft(e.Device, overWifi: true);
            }
        };
        _wifi.MessageReceived += (sender, e) => OnMessage(new ChannelMessage(e.Device, e.Envelope, OverWifi: true));
        _wifi.Paired += (sender, e) => OnPaired(e.Device);
        _status.ActiveChanged += (sender, kind) => Changed?.Invoke(this, EventArgs.Empty);
        _ble.HelloReceived += (sender, e) => OnHello(e.Device, e.Hello, overWifi: false);
        _ble.SessionClosed += (sender, device) => OnLeft(device, overWifi: false);
        _ble.MessageReceived += (sender, e) => OnMessage(new ChannelMessage(e.Device, e.Envelope, OverWifi: false));
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        // An Android phone is paired as long as its record is; the iPhone is paired in Windows Bluetooth settings.
        if (_devices.All.LastOrDefault(d => d.Platform == DevicePlatform.Android) is { } android)
        {
            OnPaired(android);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a message to the phone, over Wi‑Fi when it is up and over Bluetooth otherwise.
    /// </summary>
    /// <param name="envelope">Message; version and id are filled in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when the phone is not connected or the message does not fit the Bluetooth channel.</returns>
    public async Task<bool> SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        if (Phone is not { } phone)
        {
            return false;
        }

        return (_wifiHellos.ContainsKey(phone.DeviceId) && await _wifi.SendAsync(phone.DeviceId, envelope, cancellationToken)) || _ble.Send(phone.DeviceId, envelope);
    }

    /// <summary>
    /// Gets a value indicating whether Fenpo on an Android phone is connected, in use or not.
    /// </summary>
    public bool AndroidConnected => _connected.Values.Any(d => KindOf(HelloOf(d.DeviceId)) == PhoneKind.Android);

    /// <summary>
    /// Sends a message to the Android phone, in use or not (its notifications and player answer to it).
    /// </summary>
    /// <param name="envelope">Message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when it is not connected.</returns>
    public async Task<bool> SendToAndroidAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        if (_connected.Values.FirstOrDefault(d => KindOf(HelloOf(d.DeviceId)) == PhoneKind.Android) is not { } phone)
        {
            return false;
        }

        return (_wifiHellos.ContainsKey(phone.DeviceId) && await _wifi.SendAsync(phone.DeviceId, envelope, cancellationToken)) || _ble.Send(phone.DeviceId, envelope);
    }

    /// <summary>
    /// Sends a message over Wi‑Fi only (file transfers).
    /// </summary>
    /// <param name="envelope">Message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when the phone is not connected over Wi‑Fi.</returns>
    public Task<bool> SendOverWifiAsync(Envelope envelope, CancellationToken cancellationToken = default) => Phone is { } phone ? _wifi.SendAsync(phone.DeviceId, envelope, cancellationToken) : Task.FromResult(false);

    /// <summary>
    /// Answers a request on the channel it came on, so the phone can match <c>reply_to</c>; the other channel if that
    /// one is gone.
    /// </summary>
    /// <param name="request">Request.</param>
    /// <param name="reply">Answer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when the phone could not be reached.</returns>
    public async Task<bool> ReplyAsync(ChannelMessage request, Envelope reply, CancellationToken cancellationToken = default)
    {
        reply.ReplyTo = request.Envelope.Id;
        var deviceId = request.Device.DeviceId;

        if (request.OverWifi)
        {
            return await _wifi.SendAsync(deviceId, reply, cancellationToken) || _ble.Send(deviceId, reply);
        }

        return _ble.Send(deviceId, reply) || await _wifi.SendAsync(deviceId, reply, cancellationToken);
    }

    private static PhoneKind? KindOf(Hello? hello) => hello?.Platform switch
    {
        DevicePlatform.Ios => PhoneKind.Iphone,
        DevicePlatform.Android => PhoneKind.Android,
        _ => null,
    };

    private Hello? HelloOf(string deviceId) => _wifiHellos.TryGetValue(deviceId, out var wifi) ? wifi : _bleHellos.TryGetValue(deviceId, out var ble) ? ble : null;

    private void OnPaired(PairedAppDevice device)
    {
        if (device.Platform != DevicePlatform.Android)
        {
            return;
        }

        _status.Update(PhoneKind.Android, s => s with
        {
            HasAppPhone = true,
            Name = device.Name,
            State = s.State == PhoneConnectionState.NotPaired ? PhoneConnectionState.Disconnected : s.State,
        });
    }

    private void OnHello(PairedAppDevice device, Hello hello, bool overWifi)
    {
        (overWifi ? _wifiHellos : _bleHellos)[device.DeviceId] = hello;
        _connected[device.DeviceId] = device;

        // Records from before platforms were kept learn theirs here.
        if (device.Platform != hello.Platform && hello.Platform != DevicePlatform.Unspecified)
        {
            _devices.Save(device with { Platform = hello.Platform });
        }

        if (KindOf(hello) is { } kind)
        {
            var android = kind == PhoneKind.Android;
            _status.Update(kind, s => s with
            {
                AppConnected = true,
                HasAppPhone = true,
                WifiConnected = _wifiHellos.ContainsKey(device.DeviceId),
                OsVersion = string.IsNullOrEmpty(hello.OsVersion) ? s.OsVersion : hello.OsVersion,

                // An Android phone has no Bluetooth link of its own here: Fenpo on it is the connection.
                Name = android ? device.Name : s.Name,
                State = android ? PhoneConnectionState.Connected : s.State,
                ModelIdentifier = android && !string.IsNullOrEmpty(hello.Model) ? hello.Model : s.ModelIdentifier,
            });
        }

        // The phone keeps these and tries Wi‑Fi again right away, which helps after the PC's address changed.
        if (!overWifi)
        {
            SendAddresses(device);
        }

        HelloReceived?.Invoke(this, new ChannelMessage(device, new Envelope { Hello = hello }, overWifi));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnLeft(PairedAppDevice device, bool overWifi)
    {
        (overWifi ? _wifiHellos : _bleHellos).TryRemove(device.DeviceId, out var hello);
        var kind = KindOf(hello ?? HelloOf(device.DeviceId)) ?? (device.Platform == DevicePlatform.Android ? PhoneKind.Android : PhoneKind.Iphone);
        var connected = _wifiHellos.ContainsKey(device.DeviceId) || _bleHellos.ContainsKey(device.DeviceId);

        if (!connected)
        {
            _connected.TryRemove(device.DeviceId, out _);
        }

        _logger.LogInformation("Fenpo on {Phone} left the {Channel} channel; still connected: {Connected}", device.Name, overWifi ? "Wi-Fi" : "Bluetooth", connected);

        _status.Update(kind, s => s with
        {
            AppConnected = connected,
            WifiConnected = _wifiHellos.ContainsKey(device.DeviceId),
            State = kind == PhoneKind.Android && !connected ? PhoneConnectionState.Disconnected : s.State,
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMessage(ChannelMessage message)
    {
        if (message.Envelope.BodyCase == Envelope.BodyOneofCase.DeviceState)
        {
            OnDeviceState(message);
            return;
        }

        MessageReceived?.Invoke(this, message);
    }

    private void OnDeviceState(ChannelMessage message)
    {
        var state = message.Envelope.DeviceState;

        // The iPhone's battery comes from its Battery Service on the Bluetooth link already.
        if (HelloOf(message.Device.DeviceId)?.Platform != DevicePlatform.Android)
        {
            return;
        }

        _status.Update(PhoneKind.Android, s => s with
        {
            BatteryPercent = state.HasBatteryPercent ? (int)state.BatteryPercent : s.BatteryPercent,
            Charging = state.HasCharging ? state.Charging : s.Charging,
        });
    }

    private void SendAddresses(PairedAppDevice device)
    {
        if (_wifi.Port == 0)
        {
            return;
        }

        var deviceState = new DeviceState();
        deviceState.Addresses.AddRange(ControlServer.LanAddresses().Select(a => $"{a}:{_wifi.Port}"));

        if (deviceState.Addresses.Count > 0)
        {
            _ble.Send(device.DeviceId, new Envelope { DeviceState = deviceState });
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        foreach (var device in _connected.Values.Where(d => _bleHellos.ContainsKey(d.DeviceId)))
        {
            SendAddresses(device);
        }
    }
}
