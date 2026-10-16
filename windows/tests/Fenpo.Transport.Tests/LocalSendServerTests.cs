using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Fenpo.Transport.LocalSend;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fenpo.Transport.Tests;

/// <summary>
/// A LocalSend v2 sender (an HTTPS client that accepts any certificate, as LocalSend does) against the PC's server.
/// </summary>
public sealed class LocalSendServerTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fenpo-localsend-{Guid.NewGuid():N}");
    private readonly LocalSendServer _server = new(NullLogger<LocalSendServer>.Instance);
    private PcCertificate _certificate = null!;
    private HttpClient _client = null!;
    private int _port;
    private bool _accept = true;

    public ValueTask InitializeAsync()
    {
        _certificate = PcCertificate.LoadOrCreate(Path.Combine(_folder, "pc.pfx"), "TEST-PC");
        _port = FreePort();
        _server.Folder = () => Path.Combine(_folder, "in");
        _server.Ask = (offer, cancellationToken) => Task.FromResult(_accept);
        Assert.True(_server.Start("TEST-PC", _certificate.Certificate, _port));
        _client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { BaseAddress = new Uri($"https://127.0.0.1:{_port}/api/localsend/v2/") };
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
        _certificate.Dispose();

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task InfoDescribesThePc()
    {
        var info = await _client.GetFromJsonAsync<JsonElement>("info", TestContext.Current.CancellationToken);

        Assert.Equal("TEST-PC", info.GetProperty("alias").GetString());
        Assert.Equal("desktop", info.GetProperty("deviceType").GetString());
        Assert.Equal(64, info.GetProperty("fingerprint").GetString()!.Length);
    }

    [Fact]
    public async Task AcceptedFilesAreSavedUnderTheirName()
    {
        (Fenpo.Transport.LocalSend.LocalSendOffer Offer, IReadOnlyList<string> Saved)? received = null;
        _server.Received += (sender, e) => received = e;

        var prepared = await PrepareAsync(("f1", "../../escape.txt", "merhaba"), ("f2", "rapor.pdf", "%PDF"));
        Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        var answer = await prepared.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var session = answer.GetProperty("sessionId").GetString();

        foreach (var (id, body) in new[] { ("f1", "merhaba"), ("f2", "%PDF") })
        {
            var token = answer.GetProperty("files").GetProperty(id).GetString();
            using var upload = await _client.PostAsync($"upload?sessionId={session}&fileId={id}&token={token}", new ByteArrayContent(Encoding.UTF8.GetBytes(body)), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        var incoming = Path.Combine(_folder, "in");
        Assert.Equal("merhaba", File.ReadAllText(Path.Combine(incoming, "escape.txt")));
        Assert.Equal("%PDF", File.ReadAllText(Path.Combine(incoming, "rapor.pdf")));
        Assert.False(File.Exists(Path.Combine(_folder, "escape.txt")));
        Assert.Equal(2, received?.Saved.Count);
    }

    [Fact]
    public async Task ADeclinedOfferIsForbiddenAndTakesNoUpload()
    {
        _accept = false;

        using var prepared = await PrepareAsync(("f1", "a.txt", "x"));
        using var upload = await _client.PostAsync("upload?sessionId=x&fileId=f1&token=y", new ByteArrayContent([1]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, prepared.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_folder, "in")) && Directory.EnumerateFiles(Path.Combine(_folder, "in")).Any());
    }

    [Fact]
    public async Task AWrongTokenIsForbidden()
    {
        using var prepared = await PrepareAsync(("f1", "a.txt", "x"));
        var answer = await prepared.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var session = answer.GetProperty("sessionId").GetString();

        using var upload = await _client.PostAsync($"upload?sessionId={session}&fileId=f1&token=wrong", new ByteArrayContent([1]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
    }

    private Task<HttpResponseMessage> PrepareAsync(params (string Id, string Name, string Body)[] files)
    {
        var body = new
        {
            info = new { alias = "Ayşe'nin telefonu", version = "2.1", deviceModel = "Pixel", deviceType = "mobile", fingerprint = "abc", port = 53317, protocol = "https", download = false },
            files = files.ToDictionary(f => f.Id, f => new { id = f.Id, fileName = f.Name, size = Encoding.UTF8.GetByteCount(f.Body), fileType = "text/plain" }),
        };
        return _client.PostAsJsonAsync("prepare-upload", body, TestContext.Current.CancellationToken);
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
