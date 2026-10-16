using Fenpo.Core.Devices;
using Fenpo.Core.Media;
using Fenpo.Core.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// Shows only the phone in use: its notifications in the store (the other phone's are kept but hidden) and its
/// player in the media source. Messages and contacts follow it in the Bluetooth service, sends in
/// <see cref="PhoneChannel"/>.
/// </summary>
public sealed class ActivePhoneSync(IPhoneStatusSource status, NotificationStore notifications, CombinedMediaSource media, ILogger<ActivePhoneSync> logger) : IHostedService
{
    private readonly IPhoneStatusSource _status = status;
    private readonly NotificationStore _notifications = notifications;
    private readonly CombinedMediaSource _media = media;
    private readonly ILogger<ActivePhoneSync> _logger = logger;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _status.ActiveChanged += (sender, kind) => Apply(kind);
        Apply(_status.Active);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Apply(PhoneKind kind)
    {
        _notifications.ActiveSource = kind == PhoneKind.Iphone ? "i:" : "a:";
        _media.Use(kind == PhoneKind.Iphone ? 0 : 1);
        _logger.LogInformation("Showing the {Phone}'s notifications and media", kind);
    }
}
