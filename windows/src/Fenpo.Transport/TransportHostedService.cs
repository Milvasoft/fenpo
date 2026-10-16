using Fenpo.Core.Channel;
using Fenpo.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.Transport;

/// <summary>
/// Starts the ④ control server on the port phones already know and announces it over DNS-SD as <c>_fenpo._tcp</c>.
/// </summary>
public sealed class TransportHostedService(
    ControlServer server,
    ISettingsStore settings,
    PcIdentity identity,
    ILogger<TransportHostedService> logger) : IHostedService
{
    private readonly ControlServer _server = server;
    private readonly ISettingsStore _settings = settings;
    private readonly PcIdentity _identity = identity;
    private readonly ILogger<TransportHostedService> _logger = logger;
    private DnsSdRegistration? _dnsSd;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _server.Start(_settings.Current.ControlPort);

            if (_server.Port != _settings.Current.ControlPort)
            {
                _settings.Update(s => s with { ControlPort = _server.Port });
            }

            if (ControlServer.LanAddresses().FirstOrDefault() is { } address)
            {
                var host = _identity.DeviceName;
                _dnsSd = DnsSdRegistration.Register($"{host}._fenpo._tcp.local", $"{host}.local", address, (ushort)_server.Port, new Dictionary<string, string> { ["id"] = _identity.DeviceId, ["v"] = "1" }, _logger);
            }
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or UnauthorizedAccessException)
        {
            // Everything over Bluetooth keeps working; only the Wi-Fi channel is off.
            _logger.LogError(exception, "The Fenpo control server could not start");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _dnsSd?.Dispose();
        _dnsSd = null;
        await _server.DisposeAsync();
    }
}
