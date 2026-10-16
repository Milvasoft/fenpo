using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using Fenpo.Core.Settings;
using Fenpo.Protocol.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Fenpo.App.Services;

/// <summary>
/// One entry in the clipboard page's list.
/// </summary>
/// <param name="Text">Text or link; null for an image.</param>
/// <param name="ImagePng">PNG bytes of an image, or null.</param>
/// <param name="FromPhone">True when it came from the phone, false when this PC sent it.</param>
/// <param name="At">When it was copied.</param>
public sealed record ClipboardEntry(string? Text, byte[]? ImagePng, bool FromPhone, DateTimeOffset At);

/// <summary>
/// Shares the clipboard with Fenpo on the phone: what is copied on the PC goes to the phone while sharing is on, what
/// the phone sends lands on the PC's clipboard, and the phone can ask for the PC's clipboard (ClipboardRequest).
/// Content that password managers mark as private never leaves the PC.
/// </summary>
public sealed class ClipboardSync(PhoneChannel channel, ISettingsStore settings, ILogger<ClipboardSync> logger) : IHostedService
{
    private const int _maxImageBytes = 512 * 1024;
    private const int _historyLength = 20;

    // Set by password managers and other apps that do not want the content watched or synced.
    private static readonly string[] _privateFormats = ["ExcludeClipboardContentFromMonitorProcessing", "Clipboard Viewer Ignore"];

    private readonly PhoneChannel _channel = channel;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<ClipboardSync> _logger = logger;
    private readonly Lock _lock = new();
    private readonly List<ClipboardEntry> _history = [];
    private DispatcherQueue? _dispatcher;
    private string? _lastFingerprint;

    /// <summary>
    /// Raised from any thread when the list changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the latest entries, newest first. Kept in memory only.
    /// </summary>
    public IReadOnlyList<ClipboardEntry> History
    {
        get
        {
            lock (_lock)
            {
                return [.. _history];
            }
        }
    }

    /// <summary>
    /// Starts watching the clipboard on the UI thread. Call once after the main window exists.
    /// </summary>
    /// <param name="dispatcher">UI thread dispatcher.</param>
    public void AttachUi(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        dispatcher.TryEnqueue(() => Clipboard.ContentChanged += (sender, e) => _ = OnPcClipboardChangedAsync());
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => _ = OnMessageAsync(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Adds sample entries to the list, for the screenshot checks (<c>--sample</c>).
    /// </summary>
    public void ShowSamples()
    {
        Remember(new ClipboardEntry("482 913", null, FromPhone: true, DateTimeOffset.Now.AddMinutes(-14)));
        Remember(new ClipboardEntry("docker compose -f deploy.yml up -d --build", null, FromPhone: false, DateTimeOffset.Now.AddMinutes(-5)));
        Remember(new ClipboardEntry("https://www.openstreetmap.org/search?query=Moda%20Sahili", null, FromPhone: true, DateTimeOffset.Now.AddMinutes(-1)));
    }

    /// <summary>
    /// Puts an entry from the list back on the PC's clipboard (without sending it to the phone again).
    /// </summary>
    /// <param name="entry">Entry.</param>
    public void CopyToPc(ClipboardEntry entry) => _dispatcher?.TryEnqueue(() => _ = SetPcClipboardAsync(entry.Text, entry.ImagePng));

    private static string Fingerprint(string? text, byte[]? image) => text is not null ? "t:" + text : "i:" + Convert.ToHexString(SHA256.HashData(image ?? []));

    private static bool IsLink(string text) => !text.Any(char.IsWhiteSpace) && Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static async Task<byte[]?> ReadImageAsPngAsync(DataPackageView view)
    {
        var reference = await view.GetBitmapAsync();
        using var source = await reference.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(source);
        using var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var target = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, target);
        encoder.SetSoftwareBitmap(pixels);
        await encoder.FlushAsync();
        var bytes = new byte[target.Size];
        target.Seek(0);
        await target.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    private void Remember(ClipboardEntry entry)
    {
        lock (_lock)
        {
            _history.Insert(0, entry);

            if (_history.Count > _historyLength)
            {
                _history.RemoveRange(_historyLength, _history.Count - _historyLength);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // UI thread: the clipboard can only be read there.
    private async Task<(string? Text, byte[]? Image)?> ReadPcClipboardAsync()
    {
        var view = Clipboard.GetContent();

        if (_privateFormats.Any(view.Contains))
        {
            return null;
        }

        if (view.Contains(StandardDataFormats.Text))
        {
            var text = await view.GetTextAsync();
            return string.IsNullOrEmpty(text) ? null : (text, null);
        }

        if (view.Contains(StandardDataFormats.Bitmap))
        {
            var png = await ReadImageAsPngAsync(view);
            return png is { Length: > 0 and <= _maxImageBytes } ? (null, png) : null;
        }

        return null;
    }

    private static Envelope ItemEnvelope(string? text, byte[]? image)
    {
        var item = new ClipboardItem { ItemId = Guid.NewGuid().ToString(), CreatedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow) };

        if (text is null)
        {
            item.Image = new ClipboardImage { MimeType = "image/png", Data = ByteString.CopyFrom(image) };
        }
        else if (IsLink(text))
        {
            item.Url = text;
        }
        else
        {
            item.Text = text;
        }

        return new Envelope { Clipboard = item };
    }

    private async Task OnPcClipboardChangedAsync()
    {
        try
        {
            if (!_settings.Current.ShareClipboard || !_channel.IsConnected || !_channel.Supports(Capability.Clipboard))
            {
                return;
            }

            if (await ReadPcClipboardAsync() is not { } content)
            {
                return;
            }

            // Fenpo itself put this here (from the phone or the list): do not send it back.
            var fingerprint = Fingerprint(content.Text, content.Image);

            if (fingerprint == _lastFingerprint)
            {
                return;
            }

            _lastFingerprint = fingerprint;

            if (await _channel.SendAsync(ItemEnvelope(content.Text, content.Image)))
            {
                Remember(new ClipboardEntry(content.Text, content.Image, FromPhone: false, DateTimeOffset.Now));
            }
            else
            {
                _logger.LogInformation("Clipboard item not sent: too large for Bluetooth or the phone is gone");
            }
        }
        catch (Exception exception)
        {
            // The clipboard is often locked by another app for a moment; the next copy tries again.
            _logger.LogDebug(exception, "Reading the PC clipboard failed");
        }
    }

    private async Task OnMessageAsync(ChannelMessage message)
    {
        try
        {
            switch (message.Envelope.BodyCase)
            {
                case Envelope.BodyOneofCase.Clipboard:
                    await OnPhoneItemAsync(message);
                    break;
                case Envelope.BodyOneofCase.ClipboardRequest:
                    await OnPhoneRequestAsync(message);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Handling {Body} from {Phone} failed", message.Envelope.BodyCase, message.Device.Name);
        }
    }

    private async Task OnPhoneItemAsync(ChannelMessage message)
    {
        if (!_settings.Current.ShareClipboard)
        {
            await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.Declined, Message = "Clipboard sharing is off on the PC" } });
            return;
        }

        var item = message.Envelope.Clipboard;
        var (text, image) = item.ContentCase switch
        {
            ClipboardItem.ContentOneofCase.Text => (item.Text, (byte[]?)null),
            ClipboardItem.ContentOneofCase.Url => (item.Url, null),
            ClipboardItem.ContentOneofCase.Image => ((string?)null, item.Image.Data.ToByteArray()),
            _ => (null, null),
        };

        if (text is null && image is null)
        {
            return;
        }

        _logger.LogInformation("Clipboard {Kind} from {Phone}", item.ContentCase, message.Device.Name);
        _dispatcher?.TryEnqueue(() => _ = SetPcClipboardAsync(text, image));
        Remember(new ClipboardEntry(text, image, FromPhone: true, DateTimeOffset.Now));
    }

    private async Task OnPhoneRequestAsync(ChannelMessage message)
    {
        if (!_settings.Current.ShareClipboard || _dispatcher is null)
        {
            await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.Declined, Message = "Clipboard sharing is off on the PC" } });
            return;
        }

        var read = new TaskCompletionSource<(string? Text, byte[]? Image)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                read.TrySetResult(await ReadPcClipboardAsync());
            }
            catch (Exception exception)
            {
                read.TrySetException(exception);
            }
        });

        var content = await read.Task.WaitAsync(TimeSpan.FromSeconds(3));

        if (content is null)
        {
            await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.InvalidRequest, Message = "The PC clipboard holds no text or image" } });
            return;
        }

        var reply = ItemEnvelope(content.Value.Text, content.Value.Image);

        if (!await _channel.ReplyAsync(message, reply))
        {
            await _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = ErrorCode.Unsupported, Message = "The clipboard item does not fit Bluetooth" } });
            return;
        }

        Remember(new ClipboardEntry(content.Value.Text, content.Value.Image, FromPhone: false, DateTimeOffset.Now));
    }

    // UI thread.
    private async Task SetPcClipboardAsync(string? text, byte[]? image)
    {
        try
        {
            var package = new DataPackage();

            if (text is not null)
            {
                package.SetText(text);
            }
            else if (image is not null)
            {
                var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(image.AsBuffer());
                stream.Seek(0);
                package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            }

            _lastFingerprint = Fingerprint(text, image);
            Clipboard.SetContent(package);

            // Stays on the clipboard after Fenpo quits.
            Clipboard.Flush();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Setting the PC clipboard failed");
        }
    }
}
