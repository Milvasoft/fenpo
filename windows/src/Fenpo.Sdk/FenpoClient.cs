using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fenpo.Sdk;

/// <summary>
/// Typed client for Fenpo's local API. Events are raised on a background thread.
/// </summary>
/// <example>
/// <code>
/// await using var fenpo = await FenpoClient.ConnectAsync();
/// var status = await fenpo.GetStatusAsync();
/// fenpo.NotificationAdded += (sender, notification) => Console.WriteLine(notification.Title);
/// </code>
/// </example>
public sealed class FenpoClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _readLoop;
    private long _nextId;

    private FenpoClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        var utf8 = new UTF8Encoding(false);
        _reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>Raised when the phone status changes.</summary>
    public event EventHandler<PhoneStatusInfo>? StatusChanged;

    /// <summary>Raised for a new phone notification.</summary>
    public event EventHandler<NotificationInfo>? NotificationAdded;

    /// <summary>Raised when what the phone plays changes.</summary>
    public event EventHandler<MediaInfo>? MediaChanged;

    /// <summary>Raised when a call rings.</summary>
    public event EventHandler<CallInfo>? CallIncoming;

    /// <summary>Raised once when Fenpo closes the connection.</summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Connects to the running Fenpo.
    /// </summary>
    /// <param name="timeout">How long to wait for Fenpo; two seconds by default.</param>
    /// <param name="pipeName">Pipe name; Fenpo's by default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connected client.</returns>
    /// <exception cref="TimeoutException">Fenpo is not running.</exception>
    public static async Task<FenpoClient> ConnectAsync(TimeSpan? timeout = null, string pipeName = FenpoApi.PipeName, CancellationToken cancellationToken = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipe.ConnectAsync(timeout ?? TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }

        return new FenpoClient(pipe);
    }

    /// <summary>
    /// Gets the phone status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The status.</returns>
    public Task<PhoneStatusInfo> GetStatusAsync(CancellationToken cancellationToken = default) => CallAsync<PhoneStatusInfo>(FenpoApi.Status, null, cancellationToken);

    /// <summary>
    /// Gets what the phone is playing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The media state.</returns>
    public Task<MediaInfo> GetMediaStateAsync(CancellationToken cancellationToken = default) => CallAsync<MediaInfo>(FenpoApi.MediaState, null, cancellationToken);

    /// <summary>
    /// Starts playback on the phone.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the phone accepted it.</returns>
    public Task PlayAsync(CancellationToken cancellationToken = default) => CallAsync<JsonNode?>(FenpoApi.MediaPlay, null, cancellationToken);

    /// <summary>
    /// Pauses playback on the phone.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the phone accepted it.</returns>
    public Task PauseAsync(CancellationToken cancellationToken = default) => CallAsync<JsonNode?>(FenpoApi.MediaPause, null, cancellationToken);

    /// <summary>
    /// Skips to the next track on the phone.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the phone accepted it.</returns>
    public Task NextAsync(CancellationToken cancellationToken = default) => CallAsync<JsonNode?>(FenpoApi.MediaNext, null, cancellationToken);

    /// <summary>
    /// Gets the newest phone notifications.
    /// </summary>
    /// <param name="limit">Maximum count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Notifications, newest first.</returns>
    public Task<IReadOnlyList<NotificationInfo>> GetRecentNotificationsAsync(int limit = 10, CancellationToken cancellationToken = default) => CallAsync<IReadOnlyList<NotificationInfo>>(FenpoApi.NotificationsRecent, new JsonObject { ["limit"] = limit }, cancellationToken);

    /// <summary>
    /// Puts text on the phone's clipboard. Needs Fenpo on the iPhone.
    /// </summary>
    /// <param name="text">Text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the phone accepted it.</returns>
    public Task SendClipboardToPhoneAsync(string text, CancellationToken cancellationToken = default) => CallAsync<JsonNode?>(FenpoApi.ClipboardSendToPhone, new JsonObject { ["text"] = text }, cancellationToken);

    /// <summary>
    /// Makes the phone ring. Needs Fenpo on the iPhone.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the phone accepted it.</returns>
    public Task RingAsync(CancellationToken cancellationToken = default) => CallAsync<JsonNode?>(FenpoApi.Ring, null, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _pipe.DisposeAsync();

        try
        {
            await _readLoop;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Closing.
        }

        _stop.Dispose();
        _writeGate.Dispose();
    }

    private async Task<T> CallAsync<T>(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };

        if (parameters is not null)
        {
            request["params"] = parameters;
        }

        await _writeGate.WaitAsync(cancellationToken);

        try
        {
            await _writer.WriteLineAsync(request.ToJsonString().AsMemory(), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var result = await completion.Task;
        return result is null ? default! : result.Deserialize<T>(FenpoApi.JsonOptions)!;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (line.Length > 0)
                {
                    Dispatch(line);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Pipe closed.
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new IOException("Fenpo closed the connection"));
            }

            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Dispatch(string line)
    {
        JsonNode? message;

        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        if (message?["id"] is { } idNode && idNode.GetValueKind() == JsonValueKind.Number && _pending.TryRemove(idNode.GetValue<long>(), out var completion))
        {
            if (message["error"] is { } error)
            {
                completion.TrySetException(new FenpoException(error["code"]?.GetValue<int>() ?? 0, error["message"]?.GetValue<string>() ?? string.Empty));
            }
            else
            {
                completion.TrySetResult(message["result"]?.DeepClone());
            }

            return;
        }

        var parameters = message?["params"];

        switch (message?["method"]?.GetValue<string>())
        {
            case FenpoApi.StatusChanged when parameters.Deserialize<PhoneStatusInfo>(FenpoApi.JsonOptions) is { } status:
                StatusChanged?.Invoke(this, status);
                break;
            case FenpoApi.NotificationAdded when parameters.Deserialize<NotificationInfo>(FenpoApi.JsonOptions) is { } notification:
                NotificationAdded?.Invoke(this, notification);
                break;
            case FenpoApi.MediaChanged when parameters.Deserialize<MediaInfo>(FenpoApi.JsonOptions) is { } media:
                MediaChanged?.Invoke(this, media);
                break;
            case FenpoApi.CallIncoming when parameters.Deserialize<CallInfo>(FenpoApi.JsonOptions) is { } call:
                CallIncoming?.Invoke(this, call);
                break;
        }
    }
}

/// <summary>
/// An error Fenpo returned for a call.
/// </summary>
/// <param name="code">JSON-RPC error code, see <see cref="FenpoErrorCodes"/>.</param>
/// <param name="message">Error message.</param>
public sealed class FenpoException(int code, string message) : Exception(message)
{
    /// <summary>
    /// Gets the JSON-RPC error code.
    /// </summary>
    public int Code { get; } = code;
}
