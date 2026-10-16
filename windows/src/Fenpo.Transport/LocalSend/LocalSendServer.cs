using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Fenpo.Transport.LocalSend;

/// <summary>
/// LocalSend v2 (github.com/localsend/protocol) on the PC: phones and computers with the LocalSend app see this PC on
/// the network and can send it files. Discovery is UDP multicast on 224.0.0.167:53317; files come over HTTPS with a
/// self-signed certificate whose SHA-256 is the fingerprint. Every request to send is put to the PC's user first
/// (<see cref="Ask"/>); files go to <see cref="Folder"/>. Only receiving: one session at a time, no PIN.
/// </summary>
public sealed class LocalSendServer(ILogger<LocalSendServer> logger) : IAsyncDisposable
{
    /// <summary>
    /// The port LocalSend uses for discovery and HTTP.
    /// </summary>
    public const int DefaultPort = 53317;

    private static readonly IPAddress _group = IPAddress.Parse("224.0.0.167");
    private static readonly TimeSpan _askTimeout = TimeSpan.FromMinutes(1);
    private const string _api = "/api/localsend/v2/";

    private readonly ILogger<LocalSendServer> _logger = logger;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private CancellationTokenSource? _running;
    private TcpListener? _listener;
    private UdpClient? _udp;
    private X509Certificate2? _certificate;
    private LocalSendDevice _self = new();

    /// <summary>
    /// Gets or sets who decides whether an offer is accepted; without one every offer is declined.
    /// </summary>
    public Func<LocalSendOffer, CancellationToken, Task<bool>>? Ask { get; set; }

    /// <summary>
    /// Gets or sets the folder received files go to.
    /// </summary>
    public Func<string> Folder { get; set; } = () => Path.GetTempPath();

    /// <summary>
    /// Raised when every file of a session arrived, with the saved paths.
    /// </summary>
    public event EventHandler<(LocalSendOffer Offer, IReadOnlyList<string> Saved)>? Received;

    /// <summary>
    /// Gets a value indicating whether the server runs.
    /// </summary>
    public bool IsRunning => _running is not null;

    /// <summary>
    /// Starts answering LocalSend apps as <paramref name="alias"/>.
    /// </summary>
    /// <param name="alias">The name LocalSend apps show, the PC's name.</param>
    /// <param name="certificate">TLS certificate with its key.</param>
    /// <param name="port">Port for discovery and HTTPS; LocalSend apps look on <see cref="DefaultPort"/>.</param>
    /// <returns>False when the port is taken (the LocalSend app itself runs on this PC).</returns>
    public bool Start(string alias, X509Certificate2 certificate, int port = DefaultPort)
    {
        if (_running is not null)
        {
            return true;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            foreach (var address in ControlServer.LanAddresses())
            {
                _udp.JoinMulticastGroup(_group, address);
            }
        }
        catch (SocketException exception)
        {
            _logger.LogWarning("LocalSend could not start: {Reason}", exception.Message);
            _listener?.Stop();
            _udp?.Dispose();
            _listener = null;
            _udp = null;
            return false;
        }

        _certificate = certificate;
        _self = new LocalSendDevice
        {
            Alias = alias,
            DeviceModel = "Windows",
            DeviceType = "desktop",
            Fingerprint = Convert.ToHexStringLower(SHA256.HashData(certificate.RawData)),
            Port = port,
        };
        _running = new CancellationTokenSource();
        _ = AcceptLoopAsync(_running.Token);
        _ = DiscoveryLoopAsync(_running.Token);
        _ = AnnounceAsync(announce: true);
        _logger.LogInformation("LocalSend visible as {Alias} on port {Port}", alias, port);
        return true;
    }

    /// <summary>
    /// Stops answering; sessions in progress end.
    /// </summary>
    /// <returns>A task that completes when stopped.</returns>
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _running, null) is not { } running)
        {
            return;
        }

        await running.CancelAsync();
        _listener?.Stop();
        _udp?.Dispose();
        _listener = null;
        _udp = null;
        _sessions.Clear();
        running.Dispose();
        _logger.LogInformation("LocalSend stopped");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();

    // Discovery: answer every announcement, first over HTTPS (/register), else with a multicast reply.
    private async Task DiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _udp is { } udp)
        {
            try
            {
                var datagram = await udp.ReceiveAsync(cancellationToken);
                var device = JsonSerializer.Deserialize<LocalSendDevice>(datagram.Buffer, LocalSendJson.Options);

                if (device is null || device.Fingerprint == _self.Fingerprint || device.Announce != true)
                {
                    continue;
                }

                _logger.LogInformation("LocalSend device {Alias} ({Model}) announced itself", device.Alias, device.DeviceModel);
                _ = AnswerAsync(datagram.RemoteEndPoint.Address, device);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is JsonException or SocketException or ObjectDisposedException)
            {
                _logger.LogDebug("LocalSend discovery: {Reason}", exception.Message);
            }
        }
    }

    private async Task AnswerAsync(IPAddress address, LocalSendDevice device)
    {
        try
        {
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            var url = $"{device.Protocol}://{address}:{device.Port}{_api}register";
            using var response = await client.PostAsync(url, new StringContent(JsonSerializer.Serialize(_self, LocalSendJson.Options), Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug("LocalSend register at {Address} failed: {Reason}", address, exception.Message);
        }

        await AnnounceAsync(announce: false);
    }

    private async Task AnnounceAsync(bool announce)
    {
        if (_udp is not { } udp)
        {
            return;
        }

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_self with { Announce = announce }, LocalSendJson.Options);
            await udp.SendAsync(bytes, new IPEndPoint(_group, DefaultPort));
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            _logger.LogDebug("LocalSend announcement failed: {Reason}", exception.Message);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { } listener)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = ServeAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                _logger.LogDebug("LocalSend accept: {Reason}", exception.Message);
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;

        try
        {
            using (client)
            await using (var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate, ClientCertificateRequired = false }, cancellationToken);
                var http = new HttpConnection(tls);

                // Keep-alive: one request after another on the same connection.
                while (await http.ReadRequestAsync(cancellationToken) is { } request)
                {
                    await HandleAsync(http, request, remote, cancellationToken);

                    if (request.Close)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or InvalidDataException or ObjectDisposedException)
        {
            _logger.LogDebug("LocalSend connection from {Address} ended: {Reason}", remote, exception.Message);
        }
    }

    private async Task HandleAsync(HttpConnection http, HttpRequest request, IPAddress? remote, CancellationToken cancellationToken)
    {
        var path = request.Path;

        switch (request.Method, path)
        {
            case ("GET", _api + "info"):
            case ("GET", "/api/localsend/v1/info"):
                await http.DrainAsync(request, cancellationToken);
                await http.WriteJsonAsync(200, _self, cancellationToken);
                break;
            case ("POST", _api + "register"):
                await http.DrainAsync(request, cancellationToken);
                await http.WriteJsonAsync(200, _self, cancellationToken);
                break;
            case ("POST", _api + "prepare-upload"):
                await PrepareAsync(http, request, remote, cancellationToken);
                break;
            case ("POST", _api + "upload"):
                await UploadAsync(http, request, cancellationToken);
                break;
            case ("POST", _api + "cancel"):
                await http.DrainAsync(request, cancellationToken);

                if (request.Query.GetValueOrDefault("sessionId") is { } cancelled && _sessions.TryRemove(cancelled, out _))
                {
                    _logger.LogInformation("LocalSend session {Id} cancelled by the sender", cancelled);
                }

                await http.WriteStatusAsync(200, cancellationToken);
                break;
            default:
                await http.DrainAsync(request, cancellationToken);
                await http.WriteStatusAsync(404, cancellationToken);
                break;
        }
    }

    private async Task PrepareAsync(HttpConnection http, HttpRequest request, IPAddress? remote, CancellationToken cancellationToken)
    {
        var body = await http.ReadBodyAsync(request, 1024 * 1024, cancellationToken);
        var prepare = JsonSerializer.Deserialize<LocalSendPrepareUpload>(body, LocalSendJson.Options);

        if (prepare is null || prepare.Files.Count == 0)
        {
            await http.WriteStatusAsync(400, cancellationToken);
            return;
        }

        if (!_sessions.IsEmpty)
        {
            await http.WriteStatusAsync(409, cancellationToken);
            return;
        }

        var offer = new LocalSendOffer(Guid.NewGuid().ToString("N"), prepare.Info, [.. prepare.Files.Values]);
        _logger.LogInformation("LocalSend: {Alias} at {Address} wants to send {Count} files ({Bytes} bytes)", prepare.Info.Alias, remote, offer.Files.Count, offer.TotalBytes);
        var session = new Session(offer, remote, offer.Files.ToDictionary(f => f.Id, _ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))));
        _sessions[offer.SessionId] = session;
        bool accepted;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_askTimeout);
            accepted = Ask is { } ask && await ask(offer, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            accepted = false;
        }

        if (!accepted)
        {
            _sessions.TryRemove(offer.SessionId, out _);
            _logger.LogInformation("LocalSend: declined {Alias}", prepare.Info.Alias);
            await http.WriteStatusAsync(403, cancellationToken);
            return;
        }

        await http.WriteJsonAsync(200, new { sessionId = offer.SessionId, files = session.Tokens }, cancellationToken);
    }

    private async Task UploadAsync(HttpConnection http, HttpRequest request, CancellationToken cancellationToken)
    {
        var query = request.Query;

        if (!_sessions.TryGetValue(query.GetValueOrDefault("sessionId") ?? string.Empty, out var session)
            || query.GetValueOrDefault("fileId") is not { } fileId
            || !session.Tokens.TryGetValue(fileId, out var token) || token != query.GetValueOrDefault("token")
            || session.Saved.ContainsKey(fileId))
        {
            await http.DrainAsync(request, cancellationToken);
            await http.WriteStatusAsync(403, cancellationToken);
            return;
        }

        var file = session.Offer.Files.First(f => f.Id == fileId);
        var folder = Folder();
        Directory.CreateDirectory(folder);

        // Only the name: a relative folder in fileName must not lead out of the incoming folder.
        var name = Path.GetFileName(file.FileName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty);
        name = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var target = UniquePath(folder, string.IsNullOrWhiteSpace(name) ? "LocalSend" : name);
        var partial = target + ".fenpo-part";

        try
        {
            await using (var output = File.Create(partial))
            {
                await http.CopyBodyAsync(request, output, cancellationToken);
            }

            File.Move(partial, target);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }

        session.Saved[fileId] = target;
        await http.WriteStatusAsync(200, cancellationToken);

        if (session.Saved.Count == session.Offer.Files.Count && _sessions.TryRemove(session.Offer.SessionId, out _))
        {
            _logger.LogInformation("LocalSend: received {Count} files from {Alias}", session.Saved.Count, session.Offer.Sender.Alias);
            Received?.Invoke(this, (session.Offer, [.. session.Offer.Files.Select(f => session.Saved[f.Id])]));
        }
    }

    private static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);

        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({index}){Path.GetExtension(name)}");
        }

        return path;
    }

    private sealed record Session(LocalSendOffer Offer, IPAddress? Sender, Dictionary<string, string> Tokens)
    {
        public ConcurrentDictionary<string, string> Saved { get; } = new();
    }
}
