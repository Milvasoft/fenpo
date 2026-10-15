using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fenpo.Core.Devices;
using Fenpo.Core.Media;
using Fenpo.Core.Notifications;
using Fenpo.Sdk;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.Core.Api;

/// <summary>
/// Fenpo's local API for shells and other tools: newline-delimited JSON-RPC 2.0 on \\.\pipe\fenpo, open to the same
/// user only. Answers calls and pushes status, notification, media and call events to every client.
/// </summary>
public sealed class LocalApiServer(
    IPhoneStatusSource status,
    IMediaSource media,
    NotificationStore notifications,
    ILogger<LocalApiServer> logger) : IHostedService, IAsyncDisposable
{
    private const int _maxNotifications = 100;

    private readonly IPhoneStatusSource _status = status;
    private readonly IMediaSource _media = media;
    private readonly NotificationStore _notifications = notifications;
    private readonly ILogger<LocalApiServer> _logger = logger;
    private readonly Lock _clientsLock = new();
    private readonly List<ApiConnection> _clients = [];
    private CancellationTokenSource? _stop;
    private Task? _acceptLoop;

    /// <summary>
    /// Gets or sets the pipe name; Fenpo's by default, tests use their own.
    /// </summary>
    public string PipeName { get; init; } = FenpoApi.PipeName;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stop = new CancellationTokenSource();
        _status.Changed += OnStatusChanged;
        _media.Changed += OnMediaChanged;
        _notifications.Arrived += OnNotificationArrived;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stop.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _status.Changed -= OnStatusChanged;
        _media.Changed -= OnMediaChanged;
        _notifications.Arrived -= OnNotificationArrived;

        if (_stop is null)
        {
            return;
        }

        await _stop.CancelAsync();

        foreach (var client in Clients())
        {
            await client.DisposeAsync();
        }

        if (_acceptLoop is not null)
        {
            await _acceptLoop.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _stop?.Dispose();
    }

    /// <summary>
    /// Handles one request line and returns the response line, or null for a JSON-RPC notification (no id).
    /// </summary>
    /// <param name="line">Request JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response JSON or null.</returns>
    internal async Task<string?> HandleAsync(string line, CancellationToken cancellationToken)
    {
        JsonNode? request;

        try
        {
            request = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, FenpoErrorCodes.ParseError, "Parse error");
        }

        var id = request?["id"]?.DeepClone();

        if (request is not JsonObject || request["jsonrpc"]?.GetValueKind() != JsonValueKind.String || request["jsonrpc"]!.GetValue<string>() != "2.0"
            || request["method"]?.GetValueKind() != JsonValueKind.String)
        {
            return Error(id, FenpoErrorCodes.InvalidRequest, "Invalid request");
        }

        var method = request["method"]!.GetValue<string>();
        var parameters = request["params"] as JsonObject;
        JsonNode? result;

        try
        {
            result = await InvokeAsync(method, parameters, cancellationToken);
        }
        catch (ApiError error)
        {
            return id is null ? null : Error(id, error.Code, error.Message);
        }

        return id is null ? null : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();
    }

    private async Task<JsonNode?> InvokeAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case FenpoApi.Status:
                return Serialize(ToInfo(_status.Current));
            case FenpoApi.MediaState:
                return Serialize(ToInfo(_media.Current));
            case FenpoApi.MediaPlay:
                return await SendMediaAsync(MediaCommand.Play, cancellationToken);
            case FenpoApi.MediaPause:
                return await SendMediaAsync(MediaCommand.Pause, cancellationToken);
            case FenpoApi.MediaNext:
                return await SendMediaAsync(MediaCommand.NextTrack, cancellationToken);
            case FenpoApi.NotificationsRecent:
                var limit = parameters?["limit"] is { } value && value.GetValueKind() == JsonValueKind.Number ? value.GetValue<int>() : 10;

                if (limit < 1)
                {
                    throw new ApiError(FenpoErrorCodes.InvalidParams, "limit must be at least 1");
                }

                return Serialize(_notifications.Items.Take(Math.Min(limit, _maxNotifications)).Select(ToInfo).ToList());
            case FenpoApi.ClipboardSendToPhone:
                if (parameters?["text"]?.GetValueKind() != JsonValueKind.String)
                {
                    throw new ApiError(FenpoErrorCodes.InvalidParams, "text is required");
                }

                throw NeedsPhoneApp();
            case FenpoApi.Ring:
                throw NeedsPhoneApp();
            default:
                throw new ApiError(FenpoErrorCodes.MethodNotFound, $"Unknown method {method}");
        }
    }

    // Clipboard and ring go through the Fenpo channel to the iPhone app, which is not connected yet.
    private ApiError NeedsPhoneApp() => _status.Current.State != PhoneConnectionState.Connected
        ? new ApiError(FenpoErrorCodes.PhoneUnavailable, "No phone connected")
        : new ApiError(FenpoErrorCodes.NeedsPhoneApp, "Needs Fenpo on the iPhone");

    private async Task<JsonNode?> SendMediaAsync(MediaCommand command, CancellationToken cancellationToken)
    {
        if (!await _media.SendAsync(command, cancellationToken))
        {
            throw new ApiError(FenpoErrorCodes.PhoneUnavailable, "The phone did not take the command");
        }

        return null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;

            try
            {
                // CurrentUserOnly limits the pipe to this user (ACL plus an owner check on connect).
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                var client = new ApiConnection(pipe);
                pipe = null;

                lock (_clientsLock)
                {
                    _clients.Add(client);
                }

                _ = Task.Run(() => ServeAsync(client, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException exception)
            {
                // Another Fenpo owns the name, or the pipe broke while connecting.
                _logger.LogWarning(exception, "Local API pipe could not accept a client");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
            finally
            {
                if (pipe is not null)
                {
                    await pipe.DisposeAsync();
                }
            }
        }
    }

    private async Task ServeAsync(ApiConnection client, CancellationToken cancellationToken)
    {
        try
        {
            while (await client.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length > 0 && await HandleAsync(line, cancellationToken) is { } response)
                {
                    await client.WriteLineAsync(response, cancellationToken);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Client left.
        }
        finally
        {
            lock (_clientsLock)
            {
                _clients.Remove(client);
            }

            await client.DisposeAsync();
        }
    }

    private void OnStatusChanged(object? sender, PhoneStatus status) => Broadcast(FenpoApi.StatusChanged, ToInfo(status));

    private void OnMediaChanged(object? sender, NowPlaying nowPlaying) => Broadcast(FenpoApi.MediaChanged, ToInfo(nowPlaying));

    private void OnNotificationArrived(object? sender, StoredNotification stored)
    {
        Broadcast(FenpoApi.NotificationAdded, ToInfo(stored));

        if (stored.Notification.Category == NotificationCategory.IncomingCall)
        {
            var notification = stored.Notification;
            Broadcast(FenpoApi.CallIncoming, new CallInfo(string.IsNullOrEmpty(notification.Title) ? notification.Message : notification.Title));
        }
    }

    private void Broadcast<T>(string method, T payload)
    {
        var line = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = Serialize(payload) }.ToJsonString();

        foreach (var client in Clients())
        {
            _ = SendEventAsync(client, line);
        }
    }

    private async Task SendEventAsync(ApiConnection client, string line)
    {
        try
        {
            await client.WriteLineAsync(line, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The client's serve loop removes it.
        }
    }

    private ApiConnection[] Clients()
    {
        lock (_clientsLock)
        {
            return [.. _clients];
        }
    }

    private static JsonNode? Serialize<T>(T value) => JsonSerializer.SerializeToNode(value, FenpoApi.JsonOptions);

    private static string Error(JsonNode? id, int code, string message) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    private static PhoneStatusInfo ToInfo(PhoneStatus status)
    {
        List<string> links = [];

        if (status.BluetoothConnected)
        {
            links.Add("bluetooth");
        }

        if (status.WifiConnected)
        {
            links.Add("wifi");
        }

        if (status.UsbConnected)
        {
            links.Add("usb");
        }

        return new PhoneStatusInfo(status.State == PhoneConnectionState.Connected, status.Name, status.BatteryPercent, links);
    }

    private static MediaInfo ToInfo(NowPlaying nowPlaying) => new(
        nowPlaying.HasTrack,
        nowPlaying.State == PlaybackState.Playing,
        nowPlaying.Title,
        nowPlaying.Artist,
        nowPlaying.Album,
        nowPlaying.HasTrack ? nowPlaying.PositionAt(DateTimeOffset.Now).TotalSeconds : null,
        nowPlaying.Duration?.TotalSeconds);

    private static NotificationInfo ToInfo(StoredNotification stored) => new(
        stored.Id,
        stored.Notification.AppName,
        stored.Notification.Title,
        stored.Notification.Message,
        stored.Notification.Date ?? stored.Notification.ReceivedAt);

    private sealed class ApiError(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private sealed class ApiConnection(NamedPipeServerStream pipe) : IAsyncDisposable
    {
        private static readonly UTF8Encoding _utf8 = new(false);

        private readonly NamedPipeServerStream _pipe = pipe;
        private readonly StreamReader _reader = new(pipe, _utf8, false, 4096, leaveOpen: true);
        private readonly StreamWriter _writer = new(pipe, _utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => _reader.ReadLineAsync(cancellationToken);

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            await _writeGate.WaitAsync(cancellationToken);

            try
            {
                await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async ValueTask DisposeAsync() => await _pipe.DisposeAsync();
    }
}
