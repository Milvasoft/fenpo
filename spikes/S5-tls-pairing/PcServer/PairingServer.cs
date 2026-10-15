using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using Fenpo.Protocol.V1;
using Google.Protobuf;

namespace Fenpo.Spikes.S5;

/// <summary>
/// PC side of the Fenpo data channel: TLS 1.3 with mutual certificates, one-time pairing tokens,
/// PairingRequest/PairingResult, then Hello and Ping on the same connection.
/// </summary>
internal sealed class PairingServer(Identity identity, string pcName, IPAddress bindAddress)
{
    private static readonly TimeSpan _tokenLifetime = TimeSpan.FromMinutes(5);

    private readonly Identity _identity = identity;
    private readonly string _pcName = pcName;
    private readonly IPAddress _bindAddress = bindAddress;
    private readonly Lock _lock = new();
    private readonly string _devicesPath = Path.Combine(SpikePaths.Server, "devices.json");
    private TcpListener? _listener;
    private byte[] _token = [];
    private DateTimeOffset _tokenCreated;
    private Dictionary<string, TrustedDevice> _devices = [];

    public event Action<PairingUri>? PairingUriChanged;

    public int Port { get; private set; }

    public IReadOnlyList<string> Addresses { get; private set; } = [];

    public void Start(IReadOnlyList<IPAddress> advertised)
    {
        if (File.Exists(_devicesPath))
        {
            _devices = JsonSerializer.Deserialize<Dictionary<string, TrustedDevice>>(File.ReadAllText(_devicesPath)) ?? [];
        }

        _listener = new TcpListener(_bindAddress, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Addresses = advertised.Select(a => $"{a}:{Port}").ToList();
        Log.Info("tls", $"Listening on {_bindAddress}:{Port}, {_devices.Count} trusted device(s), certificate {_identity.Fingerprint}");

        RotateToken();
        _ = Task.Run(AcceptLoopAsync);
    }

    public void RotateToken()
    {
        PairingUri uri;

        lock (_lock)
        {
            _token = RandomNumberGenerator.GetBytes(16);
            _tokenCreated = DateTimeOffset.UtcNow;
            uri = new PairingUri(1, _identity.Id, _pcName, Addresses, _identity.Fingerprint, _token);
        }

        File.WriteAllText(SpikePaths.CurrentPairingUri, uri.ToString());
        PairingUriChanged?.Invoke(uri);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            var client = await _listener!.AcceptTcpClientAsync();
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint;
        using var _ = client;
        await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = _identity.Certificate,
            ClientCertificateRequired = true,
            EnabledSslProtocols = SslProtocols.Tls13,
            CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,

            // Any self-signed client certificate passes the handshake; who it belongs to is checked right after.
            RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null,
        };

        var watch = Stopwatch.StartNew();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ssl.AuthenticateAsServerAsync(options, timeout.Token);
        }
        catch (Exception exception)
        {
            Log.Info("tls", $"{remote}: handshake failed after {watch.ElapsedMilliseconds} ms: {exception.GetType().Name}: {exception.InnerException?.Message ?? exception.Message}");
            return;
        }

        var clientFingerprint = Fingerprints.Of(ssl.RemoteCertificate!);
        Log.Info("tls", $"{remote}: {ssl.SslProtocol} {ssl.NegotiatedCipherSuite} in {watch.ElapsedMilliseconds} ms, client certificate {clientFingerprint[..16]}…");

        var channel = new EnvelopeChannel(ssl);

        try
        {
            var first = await channel.ReceiveAsync();

            switch (first?.BodyCase)
            {
                case Envelope.BodyOneofCase.PairingRequest:
                    if (!await HandlePairingAsync(channel, first, clientFingerprint))
                    {
                        return;
                    }

                    break;
                case Envelope.BodyOneofCase.Hello:
                    if (!_devices.ContainsKey(clientFingerprint))
                    {
                        Log.Info("session", $"{remote}: Hello from an unpaired certificate, rejecting");
                        await channel.SendAsync(Error(first.Id, ErrorCode.NotPaired, "Certificate is not paired with this PC"));
                        return;
                    }

                    await ReplyHelloAsync(channel, first);
                    break;
                default:
                    await channel.SendAsync(Error(first?.Id ?? 0, ErrorCode.InvalidRequest, "First message must be Hello or PairingRequest"));
                    return;
            }

            await SessionLoopAsync(channel, clientFingerprint);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidProtocolBufferException)
        {
            Log.Info("session", $"{remote}: closed ({exception.GetType().Name}: {exception.Message})");
        }
    }

    private async Task<bool> HandlePairingAsync(EnvelopeChannel channel, Envelope request, string clientFingerprint)
    {
        var pairing = request.PairingRequest;
        PairingFailure? failure;

        lock (_lock)
        {
            failure = DateTimeOffset.UtcNow - _tokenCreated > _tokenLifetime ? PairingFailure.TokenExpired
                : !CryptographicOperations.FixedTimeEquals(pairing.Token.Span, _token) ? PairingFailure.TokenInvalid
                : null;

            if (failure is null)
            {
                // One-time: the token dies the moment it is used.
                _token = RandomNumberGenerator.GetBytes(16);
                _tokenCreated = DateTimeOffset.MinValue;
            }
        }

        var claimed = Convert.ToHexStringLower(pairing.CertificateSha256.Span);
        Log.Info("pair", $"PairingRequest from \"{pairing.DeviceName}\" ({pairing.Platform}), claimed certificate {(claimed.Length == 0 ? "-" : claimed[..16] + "…")}, TLS certificate {clientFingerprint[..16]}…, claimed matches TLS: {claimed == clientFingerprint}");

        if (failure is { } reason)
        {
            Log.Info("pair", $"Rejected: {reason}");
            await channel.SendAsync(new Envelope { ReplyTo = request.Id, PairingResult = new PairingResult { Accepted = false, Failure = reason } });
            return false;
        }

        // The phone is pinned by the certificate it used on this TLS connection, never by what it claims.
        var device = new TrustedDevice(pairing.DeviceId, pairing.DeviceName, pairing.Platform.ToString(), clientFingerprint, DateTimeOffset.Now);

        lock (_lock)
        {
            _devices[clientFingerprint] = device;
            File.WriteAllText(_devicesPath, JsonSerializer.Serialize(_devices, new JsonSerializerOptions { WriteIndented = true }));
        }

        await channel.SendAsync(new Envelope
        {
            ReplyTo = request.Id,
            PairingResult = new PairingResult
            {
                Accepted = true,
                PcId = _identity.Id,
                PcName = _pcName,
                BleChannelKey = ByteString.CopyFrom(RandomNumberGenerator.GetBytes(32)),
            },
        });

        Log.Info("pair", $"Paired with \"{device.Name}\", rotating the token");
        RotateToken();
        return true;
    }

    private async Task SessionLoopAsync(EnvelopeChannel channel, string clientFingerprint)
    {
        while (await channel.ReceiveAsync() is { } envelope)
        {
            switch (envelope.BodyCase)
            {
                case Envelope.BodyOneofCase.Hello:
                    await ReplyHelloAsync(channel, envelope);
                    break;
                case Envelope.BodyOneofCase.Ping:
                    await channel.SendAsync(new Envelope { ReplyTo = envelope.Id, Pong = new Pong { Nonce = envelope.Ping.Nonce } });
                    break;
                default:
                    await channel.SendAsync(Error(envelope.Id, ErrorCode.Unsupported, $"{envelope.BodyCase} is not handled by the S5 spike"));
                    break;
            }
        }

        Log.Info("session", $"{clientFingerprint[..16]}…: closed by peer");
    }

    private async Task ReplyHelloAsync(EnvelopeChannel channel, Envelope request)
    {
        var hello = request.Hello;
        var common = hello.ProtocolVersions.Where(v => v == 1).DefaultIfEmpty(0u).Max();
        var shared = hello.Capabilities.Intersect(Capabilities).ToList();
        Log.Info("session", $"Hello from \"{hello.DeviceName}\" ({hello.Platform} {hello.AppVersion}), versions [{string.Join(",", hello.ProtocolVersions)}] → {common}, shared capabilities {string.Join(",", shared)}");

        var reply = new Hello
        {
            DeviceId = _identity.Id,
            DeviceName = _pcName,
            Platform = DevicePlatform.Windows,
            AppVersion = "0.0.0-s5",
        };
        reply.ProtocolVersions.Add(1u);
        reply.Capabilities.AddRange(Capabilities);
        await channel.SendAsync(new Envelope { ReplyTo = request.Id, Hello = reply });
    }

    private static Capability[] Capabilities => [Capability.Clipboard, Capability.FileTransfer, Capability.OpenUrl, Capability.Ring, Capability.LockPc, Capability.DeviceState];

    private static Envelope Error(ulong replyTo, ErrorCode code, string message) => new() { ReplyTo = replyTo, Error = new Error { Code = code, Message = message } };

    private sealed record TrustedDevice(string DeviceId, string Name, string Platform, string CertificateSha256, DateTimeOffset PairedAt);
}
