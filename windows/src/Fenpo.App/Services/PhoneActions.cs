using System.Diagnostics;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// Small requests between the phone and the PC: the phone locks the PC (LockPc) and opens links on it (OpenUrl, "Open
/// on PC"); the PC rings the phone (RingDevice).
/// </summary>
public sealed class PhoneActions(PhoneChannel channel, ILogger<PhoneActions> logger) : IHostedService
{
    // Only links a browser or mail app opens; anything else could start a program or a settings page.
    private static readonly HashSet<string> _allowedSchemes = new(StringComparer.OrdinalIgnoreCase) { Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto };

    private readonly PhoneChannel _channel = channel;
    private readonly ILogger<PhoneActions> _logger = logger;

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
                    // The PC has no sound to find it by; the phone shows the error.
                    await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.Unsupported, Message = "The PC cannot ring" } });
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Handling {Body} from {Phone} failed", message.Envelope.BodyCase, message.Device.Name);
        }
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
