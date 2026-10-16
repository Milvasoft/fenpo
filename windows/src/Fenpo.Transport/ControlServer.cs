using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Fenpo.Core.Channel;
using Fenpo.Core.Pairing;
using Fenpo.Core.Transfers;
using Fenpo.Protocol.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Fenpo.Transport;

/// <summary>
/// The ④ control connection: TLS 1.3 with a client certificate required. A connection either pairs (PairingRequest with
/// the QR's one-time token; the phone's certificate is pinned from TLS, not from what it claims) and then continues as a
/// session, or opens a session directly with Hello from a pinned certificate (protocol/fenpo/v1/pairing.proto). A
/// connection whose first frame is a bare TransferStreamHeader is a file transfer's data connection instead and goes to
/// the <see cref="ITransferDataConnections"/> given to <see cref="UseTransfers"/> (protocol/README.md, File transfer).
/// </summary>
public sealed class ControlServer(
    PcCertificate certificate,
    PairingTokens tokens,
    IPairedAppDevices devices,
    IBleCounters counters,
    PcIdentity identity,
    ILogger<ControlServer> logger) : IAsyncDisposable
{
    private static readonly TimeSpan _firstMessageTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] _virtualAdapters = ["Hyper-V", "vEthernet", "WSL", "VirtualBox", "VMware", "Virtual Adapter", "Wi-Fi Direct"];

    private readonly PcCertificate _certificate = certificate;
    private readonly PairingTokens _tokens = tokens;
    private readonly IPairedAppDevices _devices = devices;
    private readonly IBleCounters _counters = counters;
    private readonly PcIdentity _identity = identity;
    private readonly ILogger<ControlServer> _logger = logger;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private ITransferDataConnections? _transfers;
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    /// <summary>
    /// Raised after a phone paired, with the platform it reported.
    /// </summary>
    public event EventHandler<(PairedAppDevice Device, DevicePlatform Platform)>? Paired;

    /// <summary>
    /// Raised when a paired phone's session opens (true) or closes (false).
    /// </summary>
    public event EventHandler<(PairedAppDevice Device, bool Connected)>? SessionChanged;

    /// <summary>
    /// Raised with the phone's Hello when its session opens, just before <see cref="SessionChanged"/>.
    /// </summary>
    public event EventHandler<(PairedAppDevice Device, Hello Hello)>? HelloReceived;

    /// <summary>
    /// Raised for session messages the server does not answer itself.
    /// </summary>
    public event EventHandler<(PairedAppDevice Device, Envelope Envelope)>? MessageReceived;

    /// <summary>
    /// Gets the port the server listens on, or 0 before it started.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Gets the QR code's content for the current token.
    /// </summary>
    /// <returns>The pairing URI.</returns>
    public PairingUri PairingUri() => new(_identity.DeviceId, _identity.DeviceName, [.. LanAddresses().Select(a => $"{a}:{Port}")], _certificate.Fingerprint, _tokens.Current);

    /// <summary>
    /// Sets who takes file transfer data connections; without one they are closed. Set before <see cref="Start"/>.
    /// </summary>
    /// <param name="transfers">File transfers.</param>
    public void UseTransfers(ITransferDataConnections transfers) => _transfers = transfers;

    /// <summary>
    /// Starts listening on every interface.
    /// </summary>
    /// <param name="preferredPort">Port to try first, so earlier QR codes and phones keep working; 0 for any.</param>
    public void Start(int preferredPort)
    {
        if (_listener is not null)
        {
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, preferredPort);
            _listener.Start();
        }
        catch (SocketException) when (preferredPort != 0)
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
        }

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _stop = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_listener, _stop.Token));
        _logger.LogInformation("Fenpo control server listening on port {Port}", Port);
    }

    /// <summary>
    /// Sends a message to a connected phone.
    /// </summary>
    /// <param name="deviceId">Phone id.</param>
    /// <param name="envelope">Message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when the phone is not connected.</returns>
    public async Task<bool> SendAsync(string deviceId, Envelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(deviceId, out var session))
        {
            return false;
        }

        try
        {
            await session.SendAsync(envelope, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _logger.LogDebug("Sending to {Phone} over Wi-Fi failed: {Reason}", session.Device.Name, exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a phone's session is open.
    /// </summary>
    /// <param name="deviceId">Phone id.</param>
    /// <returns>True when connected over Wi-Fi.</returns>
    public bool IsConnected(string deviceId) => _sessions.ContainsKey(deviceId);

    /// <summary>
    /// Gets the LAN IPv4 addresses phones can reach: adapters that are up, not loopback, tunnels or virtual switches
    /// (Hyper-V, WSL), no link-local addresses; the adapter with a default gateway first, Wi‑Fi before Ethernet.
    /// </summary>
    /// <returns>Addresses.</returns>
    public static IReadOnlyList<IPAddress> LanAddresses() => [.. NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) && !IsVirtual(n))
        .OrderByDescending(HasIpv4Gateway)
        .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !IsLinkLocal(a))
        .Distinct()];

    private static bool IsLinkLocal(IPAddress address) => address.GetAddressBytes() is [169, 254, ..];

    private static bool HasIpv4Gateway(NetworkInterface adapter) => adapter.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

    // Virtual switches and VM adapters have addresses a phone on the Wi‑Fi cannot reach.
    private static bool IsVirtual(NetworkInterface adapter) => _virtualAdapters.Any(name => adapter.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || adapter.Description.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_stop is not null)
        {
            await _stop.CancelAsync();
        }

        _listener?.Stop();
        _listener = null;

        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => ServeAsync(client, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException exception)
            {
                _logger.LogWarning(exception, "Accepting a connection failed");
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (sender, certificate, chain, errors) => certificate is not null);
        Session? session = null;

        try
        {
            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate.Certificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls13,
                    CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                },
                cancellationToken);

            // The phone is who its TLS certificate says, never what a message claims (S5).
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(ssl.RemoteCertificate!.GetRawCertData()));

            using var firstTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            firstTimeout.CancelAfter(_firstMessageTimeout);
            var firstFrame = await EnvelopeFraming.ReadFrameAsync(ssl, firstTimeout.Token);

            if (firstFrame is null)
            {
                return;
            }

            // A bare TransferStreamHeader parses as an Envelope without a body (its field 1 is a string, Envelope's a
            // number); Hello and PairingRequest always have one.
            var first = ParseEnvelope(firstFrame);

            if (first?.BodyCase is null or Envelope.BodyOneofCase.None)
            {
                await ServeDataAsync(ssl, firstFrame, fingerprint, remote, cancellationToken);
                return;
            }

            PairedAppDevice? device;

            switch (first?.BodyCase)
            {
                case Envelope.BodyOneofCase.PairingRequest:
                    device = await PairAsync(ssl, first, fingerprint, cancellationToken);

                    if (device is null)
                    {
                        return;
                    }

                    // The same connection continues as a session: the phone sends Hello next.
                    first = await EnvelopeFraming.ReadAsync(ssl, cancellationToken);

                    if (first?.BodyCase != Envelope.BodyOneofCase.Hello)
                    {
                        return;
                    }

                    break;
                case Envelope.BodyOneofCase.Hello:
                    device = _devices.All.FirstOrDefault(d => d.CertificateSha256 == fingerprint);

                    if (device is null)
                    {
                        _logger.LogInformation("Hello from an unpaired certificate at {Remote}", remote);
                        await EnvelopeFraming.WriteAsync(ssl, new Envelope { Version = 1, Id = 1, ReplyTo = first.Id, Error = new Error { Code = ErrorCode.NotPaired, Message = "Certificate is not paired with this PC" } }, cancellationToken);
                        return;
                    }

                    break;
                default:
                    return;
            }

            session = new Session(device, ssl);

            if (_sessions.TryRemove(device.DeviceId, out var older))
            {
                await older.DisposeAsync();
            }

            _sessions[device.DeviceId] = session;
            await session.SendAsync(new Envelope { ReplyTo = first.Id, Hello = PcHello.Create(_identity) }, cancellationToken);
            _logger.LogInformation("Fenpo app on {Phone} connected over Wi-Fi ({Platform} {Os})", device.Name, first.Hello.Platform, first.Hello.OsVersion);
            HelloReceived?.Invoke(this, (device, first.Hello));
            SessionChanged?.Invoke(this, (device, true));

            while (await EnvelopeFraming.ReadAsync(ssl, cancellationToken) is { } envelope)
            {
                if (envelope.BodyCase == Envelope.BodyOneofCase.Ping)
                {
                    await session.SendAsync(new Envelope { ReplyTo = envelope.Id, Pong = new Pong { Nonce = envelope.Ping.Nonce } }, cancellationToken);
                }
                else if (envelope.BodyCase != Envelope.BodyOneofCase.Pong)
                {
                    MessageReceived?.Invoke(this, (device, envelope));
                }
            }
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug("Control connection from {Remote} ended: {Reason}", remote, exception.Message);
        }
        finally
        {
            if (session is not null && _sessions.TryGetValue(session.Device.DeviceId, out var current) && ReferenceEquals(current, session))
            {
                _sessions.TryRemove(session.Device.DeviceId, out _);
                SessionChanged?.Invoke(this, (session.Device, false));
            }

            await ssl.DisposeAsync();
            client.Dispose();
        }
    }

    private static Envelope? ParseEnvelope(byte[] frame)
    {
        try
        {
            return Envelope.Parser.ParseFrom(frame);
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    private async Task ServeDataAsync(SslStream ssl, byte[] frame, string fingerprint, string remote, CancellationToken cancellationToken)
    {
        TransferStreamHeader header;

        try
        {
            header = TransferStreamHeader.Parser.ParseFrom(frame);
        }
        catch (InvalidProtocolBufferException)
        {
            _logger.LogInformation("First frame from {Remote} is neither an Envelope nor a TransferStreamHeader", remote);
            return;
        }

        var device = _devices.All.FirstOrDefault(d => d.CertificateSha256 == fingerprint);

        if (device is null || string.IsNullOrEmpty(header.TransferId) || _transfers is null)
        {
            _logger.LogInformation("Data connection from {Remote} refused: {Reason}", remote, device is null ? "certificate not paired" : _transfers is null ? "no transfers" : "no transfer id");
            return;
        }

        await _transfers.HandleAsync(device, header.TransferId, ssl, cancellationToken);
    }

    private async Task<PairedAppDevice?> PairAsync(SslStream ssl, Envelope request, string fingerprint, CancellationToken cancellationToken)
    {
        var pairing = request.PairingRequest;

        if (!_tokens.TryConsume(pairing.Token.Span))
        {
            _logger.LogInformation("Pairing from {Name} refused: token invalid or expired", pairing.DeviceName);
            await EnvelopeFraming.WriteAsync(ssl, new Envelope { Version = 1, Id = 1, ReplyTo = request.Id, PairingResult = new PairingResult { Accepted = false, Failure = PairingFailure.TokenInvalid } }, cancellationToken);
            return null;
        }

        var device = new PairedAppDevice(pairing.DeviceId, pairing.DeviceName, RandomNumberGenerator.GetBytes(BleSeal.KeyLength), fingerprint, pairing.Platform);
        _devices.Save(device);

        // A new key: the phone starts its BLE counter over, and so does the PC.
        _counters.Reset(device.DeviceId);
        await EnvelopeFraming.WriteAsync(
            ssl,
            new Envelope
            {
                Version = 1,
                Id = 1,
                ReplyTo = request.Id,
                PairingResult = new PairingResult
                {
                    Accepted = true,
                    PcId = _identity.DeviceId,
                    PcName = _identity.DeviceName,
                    BleChannelKey = Google.Protobuf.ByteString.CopyFrom(device.BleKey),
                },
            },
            cancellationToken);
        _logger.LogInformation("Paired with {Name} ({Platform})", pairing.DeviceName, pairing.Platform);
        Paired?.Invoke(this, (device, pairing.Platform));
        return device;
    }

    private sealed class Session(PairedAppDevice device, SslStream stream) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private ulong _nextId = 2;

        public PairedAppDevice Device { get; } = device;

        public async Task SendAsync(Envelope envelope, CancellationToken cancellationToken)
        {
            await _writeGate.WaitAsync(cancellationToken);

            try
            {
                envelope.Version = 1;
                envelope.Id = _nextId++;
                await EnvelopeFraming.WriteAsync(stream, envelope, cancellationToken);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async ValueTask DisposeAsync() => await stream.DisposeAsync();
    }
}
