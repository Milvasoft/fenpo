using System.Collections.Concurrent;
using Fenpo.Protocol.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// A page of the phone's photo library.
/// </summary>
/// <param name="Items">Newest first.</param>
/// <param name="HasMore">Whether older items follow.</param>
/// <param name="Access">What the phone lets Fenpo see.</param>
public sealed record PhotoPage(IReadOnlyList<PhotoItem> Items, bool HasMore, PhotoAccess Access);

/// <summary>
/// The phone's photo library from the PC (photos.proto, CAPABILITY_PHOTOS): pages of items, thumbnails as they come,
/// and originals fetched as a file transfer the PC accepts without asking. Lists and thumbnails go over Wi‑Fi only.
/// </summary>
public sealed class PhotoLibraryService(PhoneChannel channel, FileTransferService transfers, ILogger<PhotoLibraryService> logger) : IHostedService
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);
    private const uint _thumbnailSize = 256;

    private readonly PhoneChannel _channel = channel;
    private readonly FileTransferService _transfers = transfers;
    private readonly ILogger<PhotoLibraryService> _logger = logger;
    private readonly ConcurrentDictionary<string, byte[]> _thumbnails = new();

    /// <summary>
    /// Raised from any thread when a thumbnail arrived, with the item id.
    /// </summary>
    public event EventHandler<string>? ThumbnailArrived;

    /// <summary>
    /// Gets a value indicating whether the phone in use shares its photo library now (it offers it and Wi‑Fi is up).
    /// </summary>
    public bool Available => _channel.IsConnected && _channel.WifiConnected && _channel.Supports(Capability.Photos);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) =>
        {
            if (message.Envelope.BodyCase == Envelope.BodyOneofCase.PhotoThumbnail && message.Envelope.PhotoThumbnail is { Jpeg.Length: > 0 } thumbnail)
            {
                _thumbnails[thumbnail.Id] = thumbnail.Jpeg.ToByteArray();
                ThumbnailArrived?.Invoke(this, thumbnail.Id);
            }
        };

        // Another phone, or the same one after a reconnect, may have other ids.
        _channel.HelloReceived += (sender, hello) => _thumbnails.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// A thumbnail that already arrived, or null.
    /// </summary>
    /// <param name="id">Item id.</param>
    /// <returns>JPEG bytes.</returns>
    public byte[]? Thumbnail(string id) => _thumbnails.GetValueOrDefault(id);

    /// <summary>
    /// Lists the library, newest first.
    /// </summary>
    /// <param name="before">Only items taken before this; null for the newest.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, or null when the phone did not answer.</returns>
    public async Task<PhotoPage?> ListAsync(DateTimeOffset? before, int limit, CancellationToken cancellationToken = default)
    {
        var request = new PhotoListRequest { Limit = (uint)limit };

        if (before is { } time)
        {
            request.Before = Timestamp.FromDateTimeOffset(time);
        }

        var answer = await _channel.RequestOverWifiAsync(new Envelope { PhotoListRequest = request }, _timeout, cancellationToken);

        if (answer?.PhotoList is not { } list)
        {
            _logger.LogInformation("Photo list not answered: {Body} {Error}", answer?.BodyCase, answer?.Error?.Code);
            return null;
        }

        return new PhotoPage([.. list.Items], list.HasMore, list.Access);
    }

    /// <summary>
    /// Asks for the thumbnails of items not yet cached; they arrive through <see cref="ThumbnailArrived"/>.
    /// </summary>
    /// <param name="ids">Item ids.</param>
    /// <returns>A task that completes when asked.</returns>
    public async Task RequestThumbnailsAsync(IEnumerable<string> ids)
    {
        foreach (var chunk in ids.Where(id => !_thumbnails.ContainsKey(id)).Chunk(60))
        {
            var request = new PhotoThumbnailRequest { MaxPx = _thumbnailSize };
            request.Ids.AddRange(chunk);
            await _channel.SendOverWifiAsync(new Envelope { PhotoThumbnailRequest = request });
        }
    }

    /// <summary>
    /// Copies originals from the phone into <paramref name="folder"/>.
    /// </summary>
    /// <param name="ids">Item ids.</param>
    /// <param name="folder">Target folder.</param>
    /// <param name="listed">Whether the transfer shows on the Files page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved files; empty when the phone did not send them.</returns>
    public async Task<IReadOnlyList<string>> FetchAsync(IReadOnlyList<string> ids, string folder, bool listed, CancellationToken cancellationToken = default)
    {
        var transferId = Guid.NewGuid().ToString();
        var expected = _transfers.ExpectAsync(transferId, folder, listed, cancellationToken);
        var fetch = new PhotoFetch { TransferId = transferId };
        fetch.Ids.AddRange(ids);

        if (!await _channel.SendOverWifiAsync(new Envelope { PhotoFetch = fetch }, cancellationToken))
        {
            return [];
        }

        try
        {
            // The phone may need a while to export from iCloud; the transfer itself has its own timeouts.
            return await expected.WaitAsync(TimeSpan.FromMinutes(10), cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or InvalidDataException or OperationCanceledException or UnauthorizedAccessException)
        {
            _logger.LogInformation("Fetching {Count} photos failed: {Reason}", ids.Count, exception.Message);
            return [];
        }
    }
}
