using System.Collections.Concurrent;
using Fenpo.Core.Settings;
using Fenpo.Transport;
using Fenpo.Transport.LocalSend;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// LocalSend in Fenpo: runs <see cref="LocalSendServer"/> while Settings › Visible to LocalSend is on, asks the PC's
/// user about every offer with a Windows notification (Accept, Decline), saves accepted files to the incoming folder
/// and says when they arrived.
/// </summary>
public sealed class LocalSendBridge(
    LocalSendServer server,
    ISettingsStore settings,
    FileTransferService transfers,
    NotificationRouter router,
    LocalizationService localization,
    ILogger<LocalSendBridge> logger) : IHostedService
{
    /// <summary>
    /// The notification argument that carries a LocalSend session id.
    /// </summary>
    public const string Argument = "localsend";

    private readonly LocalSendServer _server = server;
    private readonly ISettingsStore _settings = settings;
    private readonly FileTransferService _transfers = transfers;
    private readonly NotificationRouter _router = router;
    private readonly LocalizationService _localization = localization;
    private readonly ILogger<LocalSendBridge> _logger = logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _questions = new();
    private readonly SemaphoreSlim _switching = new(1, 1);
    private PcCertificate? _certificate;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _server.Ask = AskAsync;
        _server.Folder = () => _transfers.IncomingFolder;
        _server.Received += (sender, e) => ShowReceived(e.Offer, e.Saved);
        _router.LocalSendAnswered += (sender, e) => Answer(e.Session, e.Accept);
        _settings.Changed += (sender, current) => _ = ApplyAsync(current.LocalSendVisible);
        await ApplyAsync(_settings.Current.LocalSendVisible);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _server.StopAsync();
        _certificate?.Dispose();
    }

    /// <summary>
    /// The PC's user answered an offer's notification.
    /// </summary>
    /// <param name="sessionId">The offer's session.</param>
    /// <param name="accept">Accept or decline.</param>
    public void Answer(string sessionId, bool accept)
    {
        if (_questions.TryRemove(sessionId, out var question))
        {
            question.TrySetResult(accept);
        }
    }

    private async Task ApplyAsync(bool visible)
    {
        await _switching.WaitAsync();

        try
        {
            if (visible == _server.IsRunning)
            {
                return;
            }

            if (!visible)
            {
                await _server.StopAsync();
                return;
            }

            // Its own certificate: LocalSend apps do not pin it, and the paired phones' one stays apart.
            _certificate ??= PcCertificate.LoadOrCreate(Path.Combine(ApplicationData.Current.LocalFolder.Path, "localsend-certificate.pfx"), Environment.MachineName);
            _server.Start(Environment.MachineName, _certificate.Certificate);
        }
        finally
        {
            _switching.Release();
        }
    }

    private async Task<bool> AskAsync(LocalSendOffer offer, CancellationToken cancellationToken)
    {
        var question = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _questions[offer.SessionId] = question;

        try
        {
            var files = offer.Files.Count == 1 ? offer.Files[0].FileName : _localization.Format("LocalSend_Files", offer.Files.Count);
            var notification = new AppNotificationBuilder()
                .AddText(_localization.Format("LocalSend_AskTitle", offer.Sender.Alias))
                .AddText(_localization.Format("LocalSend_AskText", files, ViewModels.FilesViewModel.Size((ulong)offer.TotalBytes, _localization)))
                .SetTag(offer.SessionId)
                .AddButton(new AppNotificationButton(_localization.GetString("LocalSend_Accept")).AddArgument(Argument, offer.SessionId).AddArgument("answer", "accept"))
                .AddButton(new AppNotificationButton(_localization.GetString("LocalSend_Decline")).AddArgument(Argument, offer.SessionId).AddArgument("answer", "decline"))
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);

            using (cancellationToken.Register(() => question.TrySetResult(false)))
            {
                return await question.Task;
            }
        }
        finally
        {
            _questions.TryRemove(offer.SessionId, out _);
            _ = AppNotificationManager.Default.RemoveByTagAsync(offer.SessionId).AsTask();
        }
    }

    private void ShowReceived(LocalSendOffer offer, IReadOnlyList<string> saved)
    {
        if (saved.Count == 0)
        {
            return;
        }

        try
        {
            var title = saved.Count == 1 ? _localization.Format("Toast_FileReceived", Path.GetFileName(saved[0])) : _localization.Format("Toast_FilesReceived", saved.Count);
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(_localization.Format("LocalSend_From", offer.Sender.Alias))
                .AddArgument("page", "Files")
                .AddButton(new AppNotificationButton(_localization.GetString("Toast_ShowFile")).AddArgument("reveal", saved[0]))
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Showing the LocalSend notification failed");
        }
    }
}
