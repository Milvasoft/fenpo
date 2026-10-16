using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Fenpo.Protocol.V1;
using Fenpo.Spikes.S5;
using Google.Protobuf;
using Windows.Devices.Enumeration;

// Plays the phone against PcServer on the same machine. "scenarios" runs every positive and negative case
// and prints a table; "browse" only looks for the DNS-SD record.
Console.OutputEncoding = Encoding.UTF8;

var mode = args.FirstOrDefault() ?? "scenarios";
var phone = Identity.LoadOrCreate(SpikePaths.Phone, "Fenpo phone simulator");
var results = new List<(string Name, bool Passed, string Detail)>();

if (mode == "browse")
{
    await BrowseAsync();
    return 0;
}

var uri = PairingUri.Parse(File.ReadAllText(SpikePaths.CurrentPairingUri).Trim());
Log.Info("phone", $"Pairing URI for \"{uri.Name}\" at {string.Join(", ", uri.Addresses)}, fp {uri.Fingerprint[..16]}…");

await Run("DNS-SD record is visible", BrowseAsync);
await Run("Wrong fingerprint in QR: phone aborts the handshake", async () =>
{
    var wrong = new string('0', 64);
    return await ExpectHandshakeFailureAsync(uri.Addresses[0], wrong, phone.Certificate, SslProtocols.Tls13);
});
await Run("TLS 1.2 only client is refused", () => ExpectHandshakeFailureAsync(uri.Addresses[0], uri.Fingerprint, phone.Certificate, SslProtocols.Tls12));
await Run("No client certificate is refused", () => ExpectHandshakeFailureAsync(uri.Addresses[0], uri.Fingerprint, null, SslProtocols.Tls13));
await Run("Wrong token: PairingResult TOKEN_INVALID", async () =>
{
    var result = await PairAsync(uri with { Token = new byte[16] });
    return (result?.Accepted == false && result.Failure == PairingFailure.TokenInvalid, $"{result}");
});
await Run("Valid QR: paired, then Hello and Ping on the same connection", async () =>
{
    var watch = Stopwatch.StartNew();
    await using var session = await ConnectAsync(uri.Addresses[0], uri.Fingerprint, phone.Certificate, SslProtocols.Tls13);
    var handshake = watch.ElapsedMilliseconds;
    await session.Channel.SendAsync(new Envelope { PairingRequest = PairingRequestFor(uri.Token) });
    var result = (await session.Channel.ReceiveAsync())?.PairingResult;

    if (result is not { Accepted: true } || result.PcId != uri.PcId || result.BleChannelKey.Length != 32)
    {
        return (false, $"{result}");
    }

    var hello = await HelloAsync(session.Channel);
    var pong = await PingAsync(session.Channel, 42);
    return (hello?.Platform == DevicePlatform.Windows && pong == 42, $"{session.Ssl.SslProtocol} {session.Ssl.NegotiatedCipherSuite}, handshake {handshake} ms, total {watch.ElapsedMilliseconds} ms, PC Hello \"{hello?.DeviceName}\" caps {hello?.Capabilities.Count}");
});
await Run("Reusing the consumed token: TOKEN_INVALID", async () =>
{
    var result = await PairAsync(uri);
    return (result?.Accepted == false && result.Failure == PairingFailure.TokenInvalid, $"{result}");
});
await Run("Reconnect with the paired certificate: Hello accepted", async () =>
{
    var watch = Stopwatch.StartNew();
    await using var session = await ConnectAsync(uri.Addresses[0], uri.Fingerprint, phone.Certificate, SslProtocols.Tls13);
    var hello = await HelloAsync(session.Channel);
    return (hello?.Platform == DevicePlatform.Windows, $"reconnect + Hello in {watch.ElapsedMilliseconds} ms");
});
await Run("Reconnect with an unknown certificate: Error NOT_PAIRED", async () =>
{
    var stranger = Identity.CreateEphemeral("Stranger");
    await using var session = await ConnectAsync(uri.Addresses[0], uri.Fingerprint, stranger.Certificate, SslProtocols.Tls13);
    await session.Channel.SendAsync(new Envelope { Hello = HelloMessage() });
    var reply = await session.Channel.ReceiveAsync();
    return (reply?.Error?.Code == ErrorCode.NotPaired, $"{reply}");
});

Console.WriteLine();
foreach (var (name, passed, detail) in results)
{
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}");
    Console.WriteLine($"      {detail}");
}

return results.All(r => r.Passed) ? 0 : 1;

async Task Run(string name, Func<Task<(bool, string)>> scenario)
{
    try
    {
        var (passed, detail) = await scenario();
        results.Add((name, passed, detail));
    }
    catch (Exception exception)
    {
        results.Add((name, false, $"{exception.GetType().Name}: {exception.Message}"));
    }
}

async Task<(bool, string)> ExpectHandshakeFailureAsync(string address, string fingerprint, X509Certificate2? certificate, SslProtocols protocols)
{
    try
    {
        await using var session = await ConnectAsync(address, fingerprint, certificate, protocols);

        // TLS 1.3 sends the server's verdict on the client certificate after the client finishes; a read shows it.
        await session.Channel.SendAsync(new Envelope { Hello = HelloMessage() });
        var reply = await session.Channel.ReceiveAsync();
        return (reply is null, reply is null ? "connection closed by PC" : $"unexpected reply {reply}");
    }
    catch (Exception exception) when (exception is AuthenticationException or IOException)
    {
        return (true, $"{exception.GetType().Name}: {exception.InnerException?.Message ?? exception.Message}");
    }
}

async Task<PairingResult?> PairAsync(PairingUri target)
{
    await using var session = await ConnectAsync(target.Addresses[0], target.Fingerprint, phone.Certificate, SslProtocols.Tls13);
    await session.Channel.SendAsync(new Envelope { PairingRequest = PairingRequestFor(target.Token) });
    return (await session.Channel.ReceiveAsync())?.PairingResult;
}

PairingRequest PairingRequestFor(byte[] token) => new()
{
    Token = ByteString.CopyFrom(token),
    DeviceId = phone.Id,
    DeviceName = "Phone simulator",
    Platform = DevicePlatform.Ios,
    CertificateSha256 = ByteString.CopyFrom(Convert.FromHexString(phone.Fingerprint)),
};

Hello HelloMessage()
{
    var hello = new Hello { DeviceId = phone.Id, DeviceName = "Phone simulator", Platform = DevicePlatform.Ios, AppVersion = "0.0.0-sim" };
    hello.ProtocolVersions.Add(1u);
    hello.Capabilities.AddRange([Capability.Clipboard, Capability.FileTransfer, Capability.Camera]);
    return hello;
}

async Task<Hello?> HelloAsync(EnvelopeChannel channel)
{
    await channel.SendAsync(new Envelope { Hello = HelloMessage() });
    return (await channel.ReceiveAsync())?.Hello;
}

static async Task<ulong?> PingAsync(EnvelopeChannel channel, ulong nonce)
{
    await channel.SendAsync(new Envelope { Ping = new Ping { Nonce = nonce } });
    return (await channel.ReceiveAsync())?.Pong?.Nonce;
}

static async Task<Session> ConnectAsync(string address, string expectedFingerprint, X509Certificate2? certificate, SslProtocols protocols)
{
    var separator = address.LastIndexOf(':');
    var tcp = new TcpClient();
    await tcp.ConnectAsync(address[..separator], int.Parse(address[(separator + 1)..]));
    var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);

    var options = new SslClientAuthenticationOptions
    {
        TargetHost = "fenpo",
        EnabledSslProtocols = protocols,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        ClientCertificates = certificate is null ? null : new X509CertificateCollection { certificate },

        // The only trust anchor is the fingerprint from the QR code (or the one pinned at pairing).
        RemoteCertificateValidationCallback = (_, server, _, _) => server is not null && Fingerprints.Of(server) == expectedFingerprint,
    };

    try
    {
        await ssl.AuthenticateAsClientAsync(options);
    }
    catch
    {
        await ssl.DisposeAsync();
        tcp.Dispose();
        throw;
    }

    return new Session(tcp, ssl, new EnvelopeChannel(ssl));
}

static async Task<(bool, string)> BrowseAsync()
{
    // WinRT DNS-SD browsing, the same query the Windows side of Fenpo would use to find other PCs.
    const string filter = "System.Devices.AepService.ProtocolId:={4526e8c1-8aac-4153-9b16-55e86ada0e54} AND System.Devices.Dnssd.Domain:=\"local\" AND System.Devices.Dnssd.ServiceName:=\"_fenpo._tcp\"";
    string[] properties = ["System.Devices.Dnssd.HostName", "System.Devices.Dnssd.PortNumber", "System.Devices.Dnssd.TextAttributes", "System.Devices.IpAddress"];
    var watch = Stopwatch.StartNew();
    var found = new TaskCompletionSource<string>();
    var watcher = DeviceInformation.CreateWatcher(filter, properties, DeviceInformationKind.AssociationEndpointService);

    watcher.Added += (_, info) =>
    {
        var text = info.Properties.TryGetValue("System.Devices.Dnssd.TextAttributes", out var t) && t is string[] attributes ? string.Join(";", attributes) : "-";
        var detail = $"\"{info.Name}\" host={info.Properties.GetValueOrDefault("System.Devices.Dnssd.HostName")} port={info.Properties.GetValueOrDefault("System.Devices.Dnssd.PortNumber")} txt={text} after {watch.ElapsedMilliseconds} ms";
        Log.Info("dnssd", detail);
        found.TrySetResult(detail);
    };
    watcher.EnumerationCompleted += (_, _) => Log.Info("dnssd", $"Enumeration completed after {watch.ElapsedMilliseconds} ms");
    watcher.Start();

    var completed = await Task.WhenAny(found.Task, Task.Delay(TimeSpan.FromSeconds(10)));
    watcher.Stop();
    return completed == found.Task ? (true, found.Task.Result) : (false, "no _fenpo._tcp instance within 10 s");
}

internal sealed record Session(TcpClient Tcp, SslStream Ssl, EnvelopeChannel Channel) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Ssl.DisposeAsync();
        Tcp.Dispose();
    }
}
