using System.Security.Cryptography;
using System.Text.Json;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Devices.WiFi;
using Windows.Security.Credentials;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// Where joining the phone's hotspot stands.
/// </summary>
public enum HotspotJoinState
{
    /// <summary>Nothing going on.</summary>
    Idle,

    /// <summary>The phone has no hotspot name and password saved in Fenpo yet.</summary>
    NotSetUp,

    /// <summary>Looking for the network and joining it.</summary>
    Joining,

    /// <summary>Joined.</summary>
    Connected,

    /// <summary>The network did not show up within a minute (the hotspot is off).</summary>
    NotFound,

    /// <summary>The network showed up but Windows could not join it (wrong password, no Wi‑Fi adapter).</summary>
    Failed,
}

/// <summary>
/// The phone's hotspot in one click (hotspot.proto): keeps the name and password the phone sends (DPAPI, per phone),
/// asks the phone to help its user turn the hotspot on, and joins the network with Windows' Wi‑Fi API as soon as it is
/// in range.
/// </summary>
public sealed class HotspotService(PhoneChannel channel, ILogger<HotspotService> logger) : IHostedService
{
    private static readonly byte[] _entropy = "fenpo-hotspot"u8.ToArray();
    private static readonly TimeSpan _searchTime = TimeSpan.FromMinutes(1);

    private readonly PhoneChannel _channel = channel;
    private readonly ILogger<HotspotService> _logger = logger;
    private readonly string _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "hotspots.bin");
    private Dictionary<string, Details> _details = [];
    private CancellationTokenSource? _joining;

    /// <summary>
    /// Raised from any thread when the state or the saved details changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets where joining stands.
    /// </summary>
    public HotspotJoinState State { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the phone in use can share its hotspot details.
    /// </summary>
    public bool Available => _channel.IsConnected && _channel.Supports(Capability.Hotspot);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _details = Load();
        _channel.MessageReceived += (sender, message) => OnMessage(message);
        _channel.Changed += (sender, e) => Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _joining?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks the phone to turn its hotspot on and joins it once it shows up.
    /// </summary>
    /// <returns>A task that completes when joined or given up.</returns>
    public async Task JoinAsync()
    {
        if (_channel.Phone is not { } phone || !_details.TryGetValue(phone.DeviceId, out var details))
        {
            SetState(HotspotJoinState.NotSetUp);
            await _channel.SendAsync(new Envelope { HotspotRequest = new HotspotRequest() });
            return;
        }

        _joining?.Cancel();
        var joining = _joining = new CancellationTokenSource(_searchTime);
        SetState(HotspotJoinState.Joining);
        await _channel.SendAsync(new Envelope { HotspotRequest = new HotspotRequest() });

        try
        {
            if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed || (await WiFiAdapter.FindAllAdaptersAsync()).FirstOrDefault() is not { } adapter)
            {
                _logger.LogInformation("No Wi-Fi adapter Fenpo may use");
                SetState(HotspotJoinState.Failed);
                return;
            }

            while (!joining.IsCancellationRequested)
            {
                await adapter.ScanAsync();
                var network = adapter.NetworkReport.AvailableNetworks.FirstOrDefault(n => n.Ssid == details.Ssid);

                if (network is not null)
                {
                    var result = await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = details.Passphrase });
                    _logger.LogInformation("Joining the hotspot: {Status}", result.ConnectionStatus);
                    SetState(result.ConnectionStatus == WiFiConnectionStatus.Success ? HotspotJoinState.Connected : HotspotJoinState.Failed);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(3), joining.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Joining the hotspot failed");
            SetState(HotspotJoinState.Failed);
            return;
        }

        if (State == HotspotJoinState.Joining)
        {
            SetState(HotspotJoinState.NotFound);
        }
    }

    private void OnMessage(ChannelMessage message)
    {
        switch (message.Envelope.BodyCase)
        {
            case Envelope.BodyOneofCase.HotspotInfo:
                var info = message.Envelope.HotspotInfo;

                if (info.Cleared || info.Ssid.Length == 0)
                {
                    _details.Remove(message.Device.DeviceId);
                }
                else
                {
                    _details[message.Device.DeviceId] = new Details(info.Ssid, info.Passphrase);
                }

                Save();
                Changed?.Invoke(this, EventArgs.Empty);
                break;
            case Envelope.BodyOneofCase.HotspotStatus when message.Envelope.HotspotStatus.State == HotspotState.NotSetUp:
                SetState(HotspotJoinState.NotSetUp);
                break;
        }
    }

    private void SetState(HotspotJoinState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Dictionary<string, Details> Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, Details>>(ProtectedData.Unprotect(File.ReadAllBytes(_path), _entropy, DataProtectionScope.CurrentUser)) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or CryptographicException)
        {
            _logger.LogWarning(exception, "Saved hotspot details could not be read");
            return [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllBytes(_path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_details), _entropy, DataProtectionScope.CurrentUser));
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Saving the hotspot details failed");
        }
    }

    private sealed record Details(string Ssid, string Passphrase);
}
