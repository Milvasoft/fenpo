using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fenpo.Core.Channel;
using Fenpo.Core.Pairing;
using Fenpo.Core.Transfers;
using Fenpo.Protocol.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fenpo.Transport.Tests;

/// <summary>
/// The S5 scenarios against the product server, with a TLS client playing the phone over loopback.
/// </summary>
public sealed class ControlServerTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fenpo-transport-{Guid.NewGuid():N}");
    private readonly MemoryDevices _devices = new();
    private readonly PairingTokens _tokens = new();
    private readonly RecordingTransfers _transfers = new();
    private PcCertificate _pcCertificate = null!;
    private ControlServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _pcCertificate = PcCertificate.LoadOrCreate(Path.Combine(_folder, "pc.pfx"), "TEST-PC");
        _server = new ControlServer(_pcCertificate, _tokens, _devices, new NoCounters(), new PcIdentity("pc-1", "TEST-PC", "0.1.0", "Windows test", string.Empty), NullLogger<ControlServer>.Instance);
        _server.UseTransfers(_transfers);
        _server.Start(0);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        _pcCertificate.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ValidQrPairsAndTheConnectionContinuesWithHelloAndPing()
    {
        var uri = _server.PairingUri();
        using var phoneCertificate = PhoneCertificate();
        await using var phone = await ConnectAsync(phoneCertificate, uri.Fingerprint);

        var result = await RequestAsync(phone, new Envelope { Id = 1, PairingRequest = Request(uri.Token) });
        var hello = await RequestAsync(phone, new Envelope { Id = 2, Hello = new Hello { DeviceId = "phone-1", Platform = DevicePlatform.Ios, OsVersion = "iOS 27.0.1" } });
        var pong = await RequestAsync(phone, new Envelope { Id = 3, Ping = new Ping { Nonce = 77 } });

        Assert.True(result.PairingResult.Accepted);
        Assert.Equal("pc-1", result.PairingResult.PcId);
        Assert.Equal(32, result.PairingResult.BleChannelKey.Length);
        Assert.Equal(DevicePlatform.Windows, hello.Hello.Platform);
        Assert.Equal(2ul, hello.ReplyTo);
        Assert.Equal(77ul, pong.Pong.Nonce);

        var device = Assert.Single(_devices.All);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(phoneCertificate.RawData)), device.CertificateSha256);
        Assert.Equal(result.PairingResult.BleChannelKey.ToByteArray(), device.BleKey);
    }

    [Fact]
    public async Task WrongOrReusedTokenIsRefused()
    {
        var uri = _server.PairingUri();
        using var phoneCertificate = PhoneCertificate();

        await using (var phone = await ConnectAsync(phoneCertificate, uri.Fingerprint))
        {
            var wrong = await RequestAsync(phone, new Envelope { Id = 1, PairingRequest = Request(new byte[16]) });
            Assert.Equal(PairingFailure.TokenInvalid, wrong.PairingResult.Failure);
        }

        await using (var phone = await ConnectAsync(phoneCertificate, uri.Fingerprint))
        {
            Assert.True((await RequestAsync(phone, new Envelope { Id = 1, PairingRequest = Request(uri.Token) })).PairingResult.Accepted);
        }

        await using (var phone = await ConnectAsync(phoneCertificate, uri.Fingerprint))
        {
            var reused = await RequestAsync(phone, new Envelope { Id = 1, PairingRequest = Request(uri.Token) });
            Assert.False(reused.PairingResult.Accepted);
        }
    }

    [Fact]
    public async Task PairedCertificateReconnectsUnknownOneIsRefused()
    {
        var uri = _server.PairingUri();
        using var paired = PhoneCertificate();
        using var stranger = PhoneCertificate();

        await using (var phone = await ConnectAsync(paired, uri.Fingerprint))
        {
            await RequestAsync(phone, new Envelope { Id = 1, PairingRequest = Request(uri.Token) });
        }

        await using (var phone = await ConnectAsync(paired, uri.Fingerprint))
        {
            Assert.Equal("TEST-PC", (await RequestAsync(phone, new Envelope { Id = 1, Hello = new Hello { DeviceId = "phone-1" } })).Hello.DeviceName);
        }

        await using (var phone = await ConnectAsync(stranger, uri.Fingerprint))
        {
            Assert.Equal(ErrorCode.NotPaired, (await RequestAsync(phone, new Envelope { Id = 1, Hello = new Hello { DeviceId = "x" } })).Error.Code);
        }
    }

    [Fact]
    public async Task AndroidKeystoreStyleCertificatePairs()
    {
        // Android Keystore's default certificate (ANDROID.md A3): CN=fake, serial 1, 1970–2048, no SAN, no EKU.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=fake", key, HashAlgorithmName.SHA256);
        using var created = request.Create(request.SubjectName, X509SignatureGenerator.CreateForECDsa(key), new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2048, 1, 1, 0, 0, 0, TimeSpan.Zero), [1]);
        using var keystoreCertificate = X509CertificateLoader.LoadPkcs12(created.CopyWithPrivateKey(key).Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
        var uri = _server.PairingUri();

        await using var phone = await ConnectAsync(keystoreCertificate, uri.Fingerprint);
        var result = await RequestAsync(phone, new Envelope { Id = 1, PairingRequest = Request(uri.Token) });
        var hello = await RequestAsync(phone, new Envelope { Id = 2, Hello = new Hello { DeviceId = "phone-1", Platform = DevicePlatform.Android, OsVersion = "Android 17" } });

        Assert.True(result.PairingResult.Accepted);
        Assert.Equal(DevicePlatform.Windows, hello.Hello.Platform);
    }

    [Fact]
    public async Task DataConnectionFromAPairedPhoneReachesTheTransfers()
    {
        var uri = _server.PairingUri();
        using var phoneCertificate = PhoneCertificate();

        await using (var phone = await ConnectAsync(phoneCertificate, uri.Fingerprint))
        {
            await RequestAsync(phone, new Envelope { Id = 1, PairingRequest = Request(uri.Token) });
        }

        await using (var data = await ConnectAsync(phoneCertificate, uri.Fingerprint))
        {
            await WriteBareFrameAsync(data, new TransferStreamHeader { TransferId = "t-1" }.ToByteArray());
            await data.WriteAsync("hello"u8.ToArray(), TestContext.Current.CancellationToken);
            await data.FlushAsync(TestContext.Current.CancellationToken);

            var (device, transferId, bytes) = await _transfers.Received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("phone-1", device.DeviceId);
            Assert.Equal("t-1", transferId);
            Assert.Equal("hello", bytes);
        }
    }

    [Fact]
    public async Task DataConnectionFromAnUnpairedCertificateIsClosed()
    {
        using var stranger = PhoneCertificate();
        await using var data = await ConnectAsync(stranger, _server.PairingUri().Fingerprint);

        await WriteBareFrameAsync(data, new TransferStreamHeader { TransferId = "t-1" }.ToByteArray());

        Assert.Equal(0, await data.ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.False(_transfers.Received.Task.IsCompleted);
    }

    [Fact]
    public async Task Tls12IsRefused()
    {
        using var phoneCertificate = PhoneCertificate();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var phone = await ConnectAsync(phoneCertificate, _server.PairingUri().Fingerprint, SslProtocols.Tls12);
            await RequestAsync(phone, new Envelope { Id = 1, Hello = new Hello() });
        });
    }

    [Fact]
    public async Task PhoneRejectsAServerWithAnotherFingerprint()
    {
        using var phoneCertificate = PhoneCertificate();

        await Assert.ThrowsAsync<AuthenticationException>(() => ConnectAsync(phoneCertificate, new byte[32]));
    }

    [Fact]
    public void QrUriRoundTrips()
    {
        var uri = _server.PairingUri();

        Assert.True(PairingUri.TryParse(uri.ToString(), out var parsed));
        Assert.Equal(uri.PcId, parsed!.PcId);
        Assert.Equal(uri.Fingerprint, parsed.Fingerprint);
        Assert.Equal(uri.Token, parsed.Token);
        Assert.All(parsed.Addresses, a => Assert.EndsWith($":{_server.Port}", a, StringComparison.Ordinal));
        Assert.True(uri.ToString().Length < 250);
    }

    private static PairingRequest Request(byte[] token) => new()
    {
        Token = ByteString.CopyFrom(token),
        DeviceId = "phone-1",
        DeviceName = "bugiphone",
        Platform = DevicePlatform.Ios,
    };

    private static X509Certificate2 PhoneCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var created = new CertificateRequest("CN=phone", key, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
    }

    private async Task<SslStream> ConnectAsync(X509Certificate2 phoneCertificate, byte[] expectedFingerprint, SslProtocols protocols = SslProtocols.Tls13)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _server.Port);
        var ssl = new SslStream(tcp.GetStream(), false, (sender, certificate, chain, errors) => certificate is not null && SHA256.HashData(certificate.GetRawCertData()).AsSpan().SequenceEqual(expectedFingerprint));
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "fenpo",
            ClientCertificates = [phoneCertificate],
            EnabledSslProtocols = protocols,
        });
        return ssl;
    }

    private static async Task<Envelope> RequestAsync(Stream phone, Envelope request)
    {
        request.Version = 1;
        await EnvelopeFraming.WriteAsync(phone, request);
        return await EnvelopeFraming.ReadAsync(phone) ?? throw new EndOfStreamException("The PC closed the connection");
    }

    private static async Task WriteBareFrameAsync(Stream stream, byte[] body)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)body.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private sealed class RecordingTransfers : ITransferDataConnections
    {
        public TaskCompletionSource<(PairedAppDevice Device, string TransferId, string Bytes)> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(PairedAppDevice device, string transferId, Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[5];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            Received.TrySetResult((device, transferId, System.Text.Encoding.ASCII.GetString(buffer)));
        }
    }

    private sealed class NoCounters : IBleCounters
    {
        public ulong NextSend(string deviceId) => 1;

        public bool TryAcceptReceived(string deviceId, ulong counter) => true;

        public void Reset(string deviceId)
        {
        }
    }

    private sealed class MemoryDevices : IPairedAppDevices
    {
        public void Remove(string deviceId)
        {
        }

        private readonly List<PairedAppDevice> _devices = [];

        public IReadOnlyList<PairedAppDevice> All
        {
            get
            {
                lock (_devices)
                {
                    return [.. _devices];
                }
            }
        }

        public void Save(PairedAppDevice device)
        {
            lock (_devices)
            {
                _devices.RemoveAll(d => d.DeviceId == device.DeviceId);
                _devices.Add(device);
            }
        }
    }
}
