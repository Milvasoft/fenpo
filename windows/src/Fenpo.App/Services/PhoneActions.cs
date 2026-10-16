using System.Diagnostics;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Fenpo.App.Services;

/// <summary>
/// Small requests between the phone and the PC: the phone locks the PC (LockPc), opens links on it (OpenUrl, "Open
/// on PC") and rings it to find it; the PC rings the phone (RingDevice).
/// </summary>
public sealed class PhoneActions(PhoneChannel channel, LocalizationService localization, ILogger<PhoneActions> logger) : IHostedService
{
    /// <summary>
    /// Tag of the ringing notification on the PC.
    /// </summary>
    public const string RingTag = "ring";

    private static readonly TimeSpan _ringLength = TimeSpan.FromSeconds(30);

    // Only links a browser or mail app opens; anything else could start a program or a settings page.
    private static readonly HashSet<string> _allowedSchemes = new(StringComparer.OrdinalIgnoreCase) { Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto };

    private readonly PhoneChannel _channel = channel;
    private readonly LocalizationService _localization = localization;
    private readonly ILogger<PhoneActions> _logger = logger;
    private CancellationTokenSource? _ringing;

    /// <summary>
    /// Gets a value indicating whether the phone can be rung (Fenpo on it offered RING).
    /// </summary>
    public bool CanRing => _channel.IsConnected && _channel.Supports(Capability.Ring);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => _ = OnMessageAsync(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Plays a sound on the phone so it can be found, or stops it.
    /// </summary>
    /// <param name="stop">True stops a ring in progress.</param>
    /// <returns>False when the phone could not be reached.</returns>
    public async Task<bool> RingAsync(bool stop = false)
    {
        if (!CanRing)
        {
            return false;
        }

        var sent = await _channel.SendAsync(new Envelope { Ring = new RingDevice { Stop = stop } });
        _logger.LogInformation("Ring {Action} sent: {Sent}", stop ? "stop" : "start", sent);
        return sent;
    }

    private async Task OnMessageAsync(ChannelMessage message)
    {
        try
        {
            switch (message.Envelope.BodyCase)
            {
                case Envelope.BodyOneofCase.LockPc:
                    _logger.LogInformation("{Phone} asked to lock the PC", message.Device.Name);

                    if (!NativeMethods.LockWorkStation())
                    {
                        _logger.LogWarning("LockWorkStation failed");
                    }

                    break;
                case Envelope.BodyOneofCase.OpenUrl:
                    await OpenAsync(message);
                    break;
                case Envelope.BodyOneofCase.Ring:
                    await RingPcAsync(message.Device.Name, message.Envelope.Ring.Stop);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Handling {Body} from {Phone} failed", message.Envelope.BodyCase, message.Device.Name);
        }
    }

    // A looping alarm notification: Windows plays the sound until "Stop", the phone's stop or half a minute.
    private async Task RingPcAsync(string phone, bool stop)
    {
        _ringing?.Cancel();
        await AppNotificationManager.Default.RemoveByTagAsync(RingTag);

        if (stop)
        {
            _logger.LogInformation("{Phone} stopped ringing the PC", phone);
            return;
        }

        _logger.LogInformation("{Phone} rings the PC", phone);
        var notification = new AppNotificationBuilder()
            .AddText(_localization.Format("Ring_PcTitle", phone))
            .AddText(_localization.GetString("Ring_PcText"))
            .SetScenario(AppNotificationScenario.Alarm)
            .SetAudioEvent(AppNotificationSoundEvent.Alarm, AppNotificationAudioLooping.Loop)
            .AddButton(new AppNotificationButton(_localization.GetString("Ring_PcStop")).AddArgument("ring", "stop"))
            .SetTag(RingTag)
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);

        _ringing = new CancellationTokenSource();
        var token = _ringing.Token;
        _ = Task.Delay(_ringLength, token).ContinueWith(_ => AppNotificationManager.Default.RemoveByTagAsync(RingTag), token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private async Task OpenAsync(ChannelMessage message)
    {
        var text = message.Envelope.OpenUrl.Url;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !_allowedSchemes.Contains(uri.Scheme))
        {
            _logger.LogInformation("OpenUrl from {Phone} refused: scheme {Scheme}", message.Device.Name, uri?.Scheme ?? "invalid");
            await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.InvalidRequest, Message = "Only http, https and mailto links open on the PC" } });
            return;
        }

        _logger.LogInformation("Opening a {Scheme} link from {Phone}", uri.Scheme, message.Device.Name);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
