using System.Collections.Concurrent;
using System.Diagnostics;
using Fenpo.App.ViewModels;
using Fenpo.Core.Channel;
using Fenpo.Core.Settings;
using Fenpo.Core.Transfers;
using Fenpo.Protocol.V1;
using Fenpo.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TransferFile = Fenpo.Protocol.V1.FileInfo;

namespace Fenpo.App.Services;

/// <summary>
/// Why sending files did not start.
/// </summary>
public enum SendFilesResult
{
    /// <summary>The offer went out.</summary>
    Started,

    /// <summary>Nothing to send (no files, or folders only).</summary>
    NothingToSend,

    /// <summary>Fenpo on the phone is not connected.</summary>
    NotConnected,

    /// <summary>Fenpo on the phone does not take files.</summary>
    Unsupported,

    /// <summary>The phone is connected over Bluetooth only; files need Wi‑Fi.</summary>
    NeedsWifi,
}

/// <summary>
/// Files between the PC and Fenpo on the phone (transfer.proto, protocol/README.md "File transfer"). Offers and
/// answers go on the control connection; the bytes on a data connection the phone opens to the control server port
/// in both directions. Files from the phone are accepted without asking (it is the user's own paired phone) and saved
/// to the incoming folder.
/// </summary>
public sealed class FileTransferService(
    PhoneChannel channel,
    ControlServer server,
    ISettingsStore settings,
    LocalizationService localization,
    ILogger<FileTransferService> logger) : IHostedService, ITransferDataConnections
{
    private static readonly TimeSpan _acceptTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _connectWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _confirmTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _progressInterval = TimeSpan.FromSeconds(1);
    private const int _historyLength = 30;

    private readonly PhoneChannel _channel = channel;
    private readonly ControlServer _server = server;
    private readonly ISettingsStore _settings = settings;
    private readonly LocalizationService _localization = localization;
    private readonly ILogger<FileTransferService> _logger = logger;
    private readonly ConcurrentDictionary<string, FileTransfer> _active = new();
    private readonly Lock _lock = new();
    private readonly List<FileTransfer> _transfers = [];

    /// <summary>
    /// Raised from any thread when a transfer was added or one changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the transfers of this session, newest first.
    /// </summary>
    public IReadOnlyList<FileTransfer> Transfers
    {
        get
        {
            lock (_lock)
            {
                return [.. _transfers];
            }
        }
    }

    /// <summary>
    /// Gets the folder received files go to.
    /// </summary>
    public string IncomingFolder => _settings.Current.IncomingFolder ?? SettingsViewModel.DefaultIncomingFolder;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => _ = OnMessageAsync(message);
        _server.UseTransfers(this);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var transfer in _active.Values)
        {
            transfer.Cancellation.Cancel();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Offers files to the phone; the transfer continues in the background.
    /// </summary>
    /// <param name="paths">Files on the PC. Folders are skipped.</param>
    /// <returns>Whether the offer went out, or why not.</returns>
    public async Task<SendFilesResult> SendAsync(IReadOnlyList<string> paths)
    {
        if (!_channel.IsConnected || _channel.Phone is not { } phone)
        {
            return SendFilesResult.NotConnected;
        }

        if (!_channel.Supports(Capability.FileTransfer))
        {
            return SendFilesResult.Unsupported;
        }

        if (!_channel.WifiConnected)
        {
            return SendFilesResult.NeedsWifi;
        }

        var existing = paths.Where(File.Exists).ToList();

        if (existing.Count == 0)
        {
            return SendFilesResult.NothingToSend;
        }

        var files = existing.Select(p => new TransferFile
        {
            FileId = Guid.NewGuid().ToString(),
            Name = Path.GetFileName(p),
            Size = (ulong)new System.IO.FileInfo(p).Length,
            MimeType = MimeTypes.Of(p),
        }).ToList();
        var transfer = new FileTransfer(Guid.NewGuid().ToString(), phone.DeviceId, incoming: false, files, existing);
        Track(transfer);

        var offer = new TransferOffer { TransferId = transfer.Id, TotalBytes = transfer.TotalBytes };
        offer.Files.AddRange(files);

        if (!await _channel.SendOverWifiAsync(new Envelope { TransferOffer = offer }))
        {
            Finish(transfer, FileTransferState.Failed);
            return SendFilesResult.NeedsWifi;
        }

        _logger.LogInformation("Offered {Count} files ({Bytes} bytes) to {Phone}", files.Count, transfer.TotalBytes, phone.Name);
        _ = RunOutgoingAsync(transfer);
        return SendFilesResult.Started;
    }

    /// <summary>
    /// Adds sample transfers to the list, for the screenshot checks (<c>--sample</c>).
    /// </summary>
    public void ShowSamples()
    {
        TransferFile File(string name, ulong size) => new() { Name = name, Size = size };

        var sending = new FileTransfer("sample-1", "sample", incoming: false, [File("Sözleşme_v3.pdf", 3_250_585)], []);
        sending.SetState(FileTransferState.InProgress);
        sending.SetProgress(2_516_582);
        var received = new FileTransfer("sample-2", "sample", incoming: true, [File("IMG_4830.MOV", 192_937_984)], []);
        received.SetState(FileTransferState.Completed);
        var failed = new FileTransfer("sample-3", "sample", incoming: false, [File("Bütçe_Ekim.xlsx", 48_128), File("Notlar.txt", 2_048)], []);
        failed.SetState(FileTransferState.Failed);

        lock (_lock)
        {
            _transfers.InsertRange(0, [sending, received, failed]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops a transfer on both sides.
    /// </summary>
    /// <param name="transfer">Transfer.</param>
    /// <returns>A task that completes when the phone was told.</returns>
    public async Task CancelAsync(FileTransfer transfer)
    {
        if (transfer.IsFinished)
        {
            return;
        }

        await transfer.Cancellation.CancelAsync();
        await _channel.SendOverWifiAsync(new Envelope { TransferCancel = new TransferCancel { TransferId = transfer.Id } });
        Finish(transfer, FileTransferState.Cancelled);
    }

    /// <summary>
    /// Shows a received file in File Explorer, or the incoming folder.
    /// </summary>
    /// <param name="path">File, or null for the folder.</param>
    public void Reveal(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        Directory.CreateDirectory(IncomingFolder);
        Process.Start(new ProcessStartInfo(IncomingFolder) { UseShellExecute = true });
    }

    /// <inheritdoc/>
    public async Task HandleAsync(PairedAppDevice device, string transferId, Stream stream, CancellationToken cancellationToken)
    {
        if (!_active.TryGetValue(transferId, out var transfer) || transfer.DeviceId != device.DeviceId || transfer.Started || transfer.IsFinished)
        {
            _logger.LogInformation("Data connection for unknown or started transfer {Id} from {Phone} closed", transferId, device.Name);
            return;
        }

        transfer.Started = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transfer.Cancellation.Token);
        transfer.SetState(FileTransferState.InProgress);

        try
        {
            if (transfer.Incoming)
            {
                await ReceiveAsync(transfer, stream, linked.Token);
            }
            else
            {
                await TransferStreams.SendAsync(stream, [.. transfer.SourcePaths.Zip(transfer.Files, (p, f) => (p, f.Size))], transfer.SetProgress, linked.Token);
                _logger.LogInformation("Wrote {Bytes} bytes of transfer {Id}", transfer.TotalBytes, transfer.Id);
            }

            transfer.DataDone.TrySetResult();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException or ObjectDisposedException)
        {
            _logger.LogWarning("Transfer {Id} broke: {Reason}", transfer.Id, exception.Message);
            transfer.DataDone.TrySetException(exception);
        }
    }

    private async Task RunOutgoingAsync(FileTransfer transfer)
    {
        var token = transfer.Cancellation.Token;

        try
        {
            if (!await transfer.Accepted.Task.WaitAsync(_acceptTimeout, token))
            {
                Finish(transfer, FileTransferState.Declined);
                return;
            }

            await WaitForDataAsync(transfer, token);

            // The receiver reports when every byte arrived and checked out.
            var confirmed = await transfer.Confirmed.Task.WaitAsync(_confirmTimeout, token);
            Finish(transfer, confirmed switch
            {
                TransferState.Completed => FileTransferState.Completed,
                TransferState.Cancelled => FileTransferState.Cancelled,
                _ => FileTransferState.Failed,
            });
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested)
            {
                _logger.LogWarning("Sending transfer {Id} failed: {Reason}", transfer.Id, exception.Message);
                await _channel.SendOverWifiAsync(new Envelope { TransferCancel = new TransferCancel { TransferId = transfer.Id } });
            }

            Finish(transfer, token.IsCancellationRequested ? FileTransferState.Cancelled : FileTransferState.Failed);
        }
    }

    // The phone must open the data connection within 30 seconds of the accept; after that the bytes may take as long
    // as they need.
    private static async Task WaitForDataAsync(FileTransfer transfer, CancellationToken cancellationToken)
    {
        var first = await Task.WhenAny(transfer.DataDone.Task, Task.Delay(_connectWindow, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();

        if (first != transfer.DataDone.Task && !transfer.Started)
        {
            throw new TimeoutException("The phone did not open the data connection");
        }

        await transfer.DataDone.Task.WaitAsync(cancellationToken);
    }

    private async Task OnMessageAsync(ChannelMessage message)
    {
        try
        {
            switch (message.Envelope.BodyCase)
            {
                case Envelope.BodyOneofCase.TransferOffer:
                    await OnOfferAsync(message);
                    break;
                case Envelope.BodyOneofCase.TransferAccept when _active.TryGetValue(message.Envelope.TransferAccept.TransferId, out var accepted):
                    accepted.Accepted.TrySetResult(message.Envelope.TransferAccept.Accepted);
                    break;
                case Envelope.BodyOneofCase.TransferProgress when _active.TryGetValue(message.Envelope.TransferProgress.TransferId, out var reported):
                    if (message.Envelope.TransferProgress.State is TransferState.Completed or TransferState.Failed or TransferState.Cancelled)
                    {
                        reported.Confirmed.TrySetResult(message.Envelope.TransferProgress.State);
                    }

                    break;
                case Envelope.BodyOneofCase.TransferCancel when _active.TryGetValue(message.Envelope.TransferCancel.TransferId, out var cancelled):
                    _logger.LogInformation("{Phone} cancelled transfer {Id}", message.Device.Name, cancelled.Id);
                    await cancelled.Cancellation.CancelAsync();
                    Finish(cancelled, FileTransferState.Cancelled);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Handling {Body} from {Phone} failed", message.Envelope.BodyCase, message.Device.Name);
        }
    }

    private async Task OnOfferAsync(ChannelMessage message)
    {
        var offer = message.Envelope.TransferOffer;
        var accept = new TransferAccept { TransferId = offer.TransferId };

        // Bytes only move over Wi‑Fi; an offer over Bluetooth alone cannot be served.
        if (offer.Files.Count == 0 || !_channel.WifiConnected || _active.ContainsKey(offer.TransferId))
        {
            _logger.LogInformation("Declined transfer {Id} from {Phone}: {Reason}", offer.TransferId, message.Device.Name, offer.Files.Count == 0 ? "no files" : "no Wi-Fi");
            await _channel.ReplyAsync(message, new Envelope { TransferAccept = accept });
            return;
        }

        var transfer = new FileTransfer(offer.TransferId, message.Device.DeviceId, incoming: true, [.. offer.Files], []);
        Track(transfer);
        accept.Accepted = true;
        accept.DataPort = 0;
        await _channel.ReplyAsync(message, new Envelope { TransferAccept = accept });
        _logger.LogInformation("Accepted {Count} files ({Bytes} bytes) from {Phone}", offer.Files.Count, transfer.TotalBytes, message.Device.Name);

        try
        {
            await WaitForDataAsync(transfer, transfer.Cancellation.Token);
            Finish(transfer, FileTransferState.Completed);
            ShowReceived(transfer);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or IOException or InvalidDataException or UnauthorizedAccessException or ObjectDisposedException)
        {
            if (!transfer.Cancellation.IsCancellationRequested)
            {
                _logger.LogWarning("Receiving transfer {Id} failed: {Reason}", transfer.Id, exception.Message);
                await _channel.SendOverWifiAsync(new Envelope { TransferProgress = new TransferProgress { TransferId = transfer.Id, BytesDone = transfer.BytesDone, State = TransferState.Failed } });
            }

            Finish(transfer, transfer.Cancellation.IsCancellationRequested ? FileTransferState.Cancelled : FileTransferState.Failed);
        }
    }

    private async Task ReceiveAsync(FileTransfer transfer, Stream stream, CancellationToken cancellationToken)
    {
        var lastReport = Stopwatch.StartNew();

        void Progress(ulong done)
        {
            transfer.SetProgress(done);

            if (lastReport.Elapsed >= _progressInterval)
            {
                lastReport.Restart();
                _ = _channel.SendOverWifiAsync(new Envelope { TransferProgress = new TransferProgress { TransferId = transfer.Id, BytesDone = done, State = TransferState.InProgress } });
            }
        }

        var saved = await TransferStreams.ReceiveAsync(stream, transfer.Files, IncomingFolder, Progress, cancellationToken);
        transfer.SetSaved(saved);
        await _channel.SendOverWifiAsync(new Envelope { TransferProgress = new TransferProgress { TransferId = transfer.Id, BytesDone = transfer.TotalBytes, State = TransferState.Completed } }, CancellationToken.None);
        _logger.LogInformation("Received {Count} files ({Bytes} bytes) into {Folder}", saved.Count, transfer.TotalBytes, IncomingFolder);
    }

    private void Track(FileTransfer transfer)
    {
        _active[transfer.Id] = transfer;
        transfer.Changed += (sender, e) => Changed?.Invoke(this, EventArgs.Empty);

        lock (_lock)
        {
            _transfers.Insert(0, transfer);

            if (_transfers.Count > _historyLength && _transfers.FindLastIndex(t => t.IsFinished) is var last and >= 0)
            {
                _transfers.RemoveAt(last);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Finish(FileTransfer transfer, FileTransferState state)
    {
        transfer.SetState(state);
        _active.TryRemove(transfer.Id, out _);
        _logger.LogInformation("Transfer {Id} ended: {State}", transfer.Id, transfer.State);
    }

    private void ShowReceived(FileTransfer transfer)
    {
        if (transfer.SavedPaths.Count == 0)
        {
            return;
        }

        try
        {
            var first = transfer.SavedPaths[0];
            var title = transfer.SavedPaths.Count == 1
                ? _localization.Format("Toast_FileReceived", Path.GetFileName(first))
                : _localization.Format("Toast_FilesReceived", transfer.SavedPaths.Count);
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(IncomingFolder)
                .AddArgument("page", "Files")
                .AddButton(new AppNotificationButton(_localization.GetString("Toast_ShowFile")).AddArgument("reveal", first))
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Showing the received-files notification failed");
        }
    }
}
