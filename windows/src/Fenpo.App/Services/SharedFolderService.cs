using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Fenpo.Core.Files;
using Fenpo.Core.Settings;
using Fenpo.Core.Transfers;
using Fenpo.Protocol.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using FileAttributes = System.IO.FileAttributes;

namespace Fenpo.App.Services;

/// <summary>
/// The PC's shared folders on the phone (files.proto, CAPABILITY_FILE_PROVIDER): answers listings, previews and
/// changes, offers the files the phone opens, takes uploads into a shared folder, and tells the phone when a folder it
/// listed changes. Only the folders picked in Settings are reachable: paths are relative to them, and anything that
/// leaves a shared folder ("..", links, absolute paths) is refused.
/// </summary>
public sealed class SharedFolderService(PhoneChannel channel, FileTransferService transfers, ISettingsStore settings, ILogger<SharedFolderService> logger) : IHostedService
{
    private const int _maxWatchers = 32;
    private const uint _maxThumbnail = 512;

    private readonly PhoneChannel _channel = channel;
    private readonly FileTransferService _transfers = transfers;
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<SharedFolderService> _logger = logger;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SharedFolder> _shared = [];

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shared = _settings.Current.SharedFolders;
        _transfers.ResolveDestination = WritableFolder;
        _channel.MessageReceived += (sender, message) => _ = OnMessageAsync(message);
        _channel.HelloReceived += (sender, hello) => StopWatching();
        _settings.Changed += (sender, current) =>
        {
            if (!ReferenceEquals(current.SharedFolders, _shared))
            {
                _shared = current.SharedFolders;
                StopWatching();
                _ = NotifyChangedAsync(string.Empty);
            }
        };
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopWatching();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The folder on the PC for a shared path, or null when the path is not inside a shared folder (or is a link).
    /// </summary>
    /// <param name="path">Path relative to the shared folders, "/" between parts; "" is not a folder on the PC.</param>
    /// <param name="root">The shared folder it is in.</param>
    /// <returns>The full path, whether or not it exists.</returns>
    public string? Resolve(string path, out SharedFolder? root) => SharedPaths.Resolve(_shared, path, SharedPaths.IsLink, out root);

    // An upload's destination: an existing folder inside a shared folder the phone may write to.
    private string? WritableFolder(string path) => Resolve(path, out var root) is { } full && root is { ReadOnly: false } && Directory.Exists(full) ? full : null;

    private async Task OnMessageAsync(ChannelMessage message)
    {
        try
        {
            switch (message.Envelope.BodyCase)
            {
                case Envelope.BodyOneofCase.FolderListRequest:
                    await ListAsync(message, message.Envelope.FolderListRequest.Path);
                    break;
                case Envelope.BodyOneofCase.FileThumbnailRequest:
                    await ThumbnailAsync(message, message.Envelope.FileThumbnailRequest);
                    break;
                case Envelope.BodyOneofCase.FileChange:
                    await ChangeAsync(message, message.Envelope.FileChange);
                    break;
                case Envelope.BodyOneofCase.FileFetch:
                    await FetchAsync(message, message.Envelope.FileFetch);
                    break;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _logger.LogInformation("Shared folder request {Body} failed: {Reason}", message.Envelope.BodyCase, exception.Message);
            await ErrorAsync(message, ErrorCode.Internal, exception.Message);
        }
    }

    private async Task ListAsync(ChannelMessage message, string path)
    {
        var listing = new FolderListing { Path = path };

        if (path.Trim('/').Length == 0)
        {
            foreach (var folder in _shared.Where(f => Directory.Exists(f.Path)))
            {
                listing.Entries.Add(new FolderEntry
                {
                    Name = folder.Name,
                    Path = folder.Name,
                    IsFolder = true,
                    ModifiedAt = Timestamp.FromDateTime(Directory.GetLastWriteTimeUtc(folder.Path)),
                    ReadOnly = folder.ReadOnly,
                });
            }

            await _channel.ReplyAsync(message, new Envelope { FolderListing = listing });
            return;
        }

        if (Resolve(path, out var root) is not { } full || !Directory.Exists(full))
        {
            await ErrorAsync(message, ErrorCode.InvalidRequest, "No such shared folder");
            return;
        }

        var prefix = path.Trim('/');

        foreach (var item in new DirectoryInfo(full).EnumerateFileSystemInfos())
        {
            // Hidden and system items, and links that could lead out of the shared folder, are not shown.
            if ((item.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
            {
                continue;
            }

            listing.Entries.Add(Entry(item, $"{prefix}/{item.Name}", root!.ReadOnly));
        }

        await _channel.ReplyAsync(message, new Envelope { FolderListing = listing });
        Watch(prefix, full);
    }

    private static FolderEntry Entry(FileSystemInfo item, string path, bool readOnly)
    {
        var isFolder = item is DirectoryInfo;
        return new FolderEntry
        {
            Name = item.Name,
            Path = path,
            IsFolder = isFolder,
            Size = item is System.IO.FileInfo file ? (ulong)file.Length : 0,
            ModifiedAt = Timestamp.FromDateTime(item.LastWriteTimeUtc),
            MimeType = isFolder ? string.Empty : MimeTypes.Of(item.Name),
            ReadOnly = readOnly || item.Attributes.HasFlag(FileAttributes.ReadOnly),
        };
    }

    private async Task ThumbnailAsync(ChannelMessage message, FileThumbnailRequest request)
    {
        var answer = new FileThumbnail { Path = request.Path };

        if (Resolve(request.Path, out _) is { } full && File.Exists(full))
        {
            var size = Math.Clamp(request.MaxPx == 0 ? 256 : request.MaxPx, 32, _maxThumbnail);
            var file = await StorageFile.GetFileFromPathAsync(full);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail);

            // Only real previews: the generic file type icon says nothing the phone does not know.
            if (thumbnail is { Type: ThumbnailType.Image })
            {
                answer.Jpeg = ByteString.CopyFrom(await ToJpegAsync(thumbnail));
            }
        }

        await _channel.ReplyAsync(message, new Envelope { FileThumbnail = answer });
    }

    private static async Task<byte[]> ToJpegAsync(IRandomAccessStream image)
    {
        var decoder = await BitmapDecoder.CreateAsync(image);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        var bytes = new byte[output.Size];
        await output.AsStreamForRead().ReadExactlyAsync(bytes);
        return bytes;
    }

    private async Task ChangeAsync(ChannelMessage message, FileChange change)
    {
        var parts = change.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var newName = change.NewName.Trim();
        var badName = change.Kind != FileChangeKind.Delete && (newName.Length == 0 || newName is "." or ".." || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0);

        // The shared folders themselves stay as they are; read-only ones are not changed at all.
        if (Resolve(change.Path, out var root) is not { } full || root is null || root.ReadOnly || badName
            || (change.Kind != FileChangeKind.CreateFolder && parts.Length < 2))
        {
            await _channel.ReplyAsync(message, new Envelope { FileChangeResult = new FileChangeResult { Ok = false } });
            return;
        }

        var result = new FileChangeResult { Ok = true };
        var parentPath = string.Join('/', parts[..^1]);

        switch (change.Kind)
        {
            case FileChangeKind.Delete when Directory.Exists(full):
                await (await StorageFolder.GetFolderFromPathAsync(full)).DeleteAsync(StorageDeleteOption.Default);
                break;
            case FileChangeKind.Delete when File.Exists(full):
                await (await StorageFile.GetFileFromPathAsync(full)).DeleteAsync(StorageDeleteOption.Default);
                break;
            case FileChangeKind.Rename when Directory.Exists(full):
                await (await StorageFolder.GetFolderFromPathAsync(full)).RenameAsync(newName, NameCollisionOption.FailIfExists);
                result.Entry = Entry(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(full)!, newName)), $"{parentPath}/{newName}", false);
                break;
            case FileChangeKind.Rename when File.Exists(full):
                await (await StorageFile.GetFileFromPathAsync(full)).RenameAsync(newName, NameCollisionOption.FailIfExists);
                result.Entry = Entry(new System.IO.FileInfo(Path.Combine(Path.GetDirectoryName(full)!, newName)), $"{parentPath}/{newName}", false);
                break;
            case FileChangeKind.CreateFolder when Directory.Exists(full) && !Path.Exists(Path.Combine(full, newName)):
                result.Entry = Entry(Directory.CreateDirectory(Path.Combine(full, newName)), $"{string.Join('/', parts)}/{newName}", false);
                break;
            default:
                result.Ok = false;
                break;
        }

        _logger.LogInformation("Shared folder change {Kind}: {Ok}", change.Kind, result.Ok);
        await _channel.ReplyAsync(message, new Envelope { FileChangeResult = result });
    }

    private async Task FetchAsync(ChannelMessage message, FileFetch fetch)
    {
        if (Resolve(fetch.Path, out _) is not { } full || !File.Exists(full) || fetch.TransferId.Length == 0)
        {
            await ErrorAsync(message, ErrorCode.InvalidRequest, "No such shared file");
            return;
        }

        if (!await _transfers.OfferRequestedAsync(full, fetch.TransferId))
        {
            await ErrorAsync(message, ErrorCode.Busy, "The file could not be offered");
        }
    }

    private Task ErrorAsync(ChannelMessage message, ErrorCode code, string text) => _channel.ReplyAsync(message, new Envelope { Error = new Error { Code = code, Message = text } });

    // Folders the phone listed are watched until it reconnects or the shared folders change; a burst of changes is one
    // FolderChanged half a second later.
    private void Watch(string path, string full)
    {
        if (_watchers.ContainsKey(path) || _watchers.Count >= _maxWatchers)
        {
            return;
        }

        var watcher = new FileSystemWatcher(full) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        FileSystemEventHandler changed = (sender, e) => Debounce(path);
        watcher.Created += changed;
        watcher.Deleted += changed;
        watcher.Changed += changed;
        watcher.Renamed += (sender, e) => Debounce(path);
        watcher.EnableRaisingEvents = true;

        if (!_watchers.TryAdd(path, watcher))
        {
            watcher.Dispose();
        }
    }

    private void Debounce(string path)
    {
        var next = new CancellationTokenSource();

        if (_pendingChanges.TryRemove(path, out var previous))
        {
            previous.Cancel();
        }

        _pendingChanges[path] = next;
        _ = Task.Delay(500, next.Token).ContinueWith(_ => NotifyChangedAsync(path), next.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private async Task NotifyChangedAsync(string path)
    {
        if (_channel.IsConnected && _channel.Supports(Capability.FileProvider))
        {
            await _channel.SendAsync(new Envelope { FolderChanged = new FolderChanged { Path = path } });
        }
    }

    private void StopWatching()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
