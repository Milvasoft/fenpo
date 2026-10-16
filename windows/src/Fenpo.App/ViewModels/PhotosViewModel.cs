using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Fenpo.Protocol.V1;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Fenpo.App.ViewModels;

/// <summary>
/// One photo or video of the phone's library on the Files page.
/// </summary>
public sealed partial class PhotoItemViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhotoItemViewModel"/> class.
    /// </summary>
    /// <param name="item">The phone's item.</param>
    public PhotoItemViewModel(PhotoItem item)
    {
        Item = item;
    }

    /// <summary>Gets the phone's item.</summary>
    public PhotoItem Item { get; }

    /// <summary>Gets the item id.</summary>
    public string Id => Item.Id;

    /// <summary>Gets the file name, for the tooltip and screen readers.</summary>
    public string Name => Item.Name;

    /// <summary>Gets a value indicating whether it is a video.</summary>
    public bool IsVideo => Item.IsVideo;

    /// <summary>Gets or sets the thumbnail once it arrived.</summary>
    [ObservableProperty]
    public partial BitmapImage? Thumbnail { get; set; }
}

/// <summary>
/// The phone's recent photos on the Files page (photos.proto): newest first, more on request; a click copies the
/// original to the incoming folder and opens it, a drag copies it where it is dropped.
/// </summary>
public sealed partial class PhotosViewModel : ObservableObject, IDisposable
{
    private const int _pageSize = 30;

    private readonly PhotoLibraryService _photos;
    private readonly PhoneChannel _channel;
    private readonly FileTransferService _transfers;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool _wasAvailable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhotosViewModel"/> class.
    /// </summary>
    /// <param name="photos">The phone's library.</param>
    /// <param name="channel">The Fenpo channel, for connection changes.</param>
    /// <param name="transfers">Where opened photos go.</param>
    /// <param name="localization">Strings.</param>
    public PhotosViewModel(PhotoLibraryService photos, PhoneChannel channel, FileTransferService transfers, LocalizationService localization)
    {
        _photos = photos;
        _channel = channel;
        _transfers = transfers;
        _localization = localization;
        _photos.ThumbnailArrived += OnThumbnailArrived;
        _channel.Changed += OnChannelChanged;
        _ = RefreshAsync();
    }

    /// <summary>Gets the items shown.</summary>
    public ObservableCollection<PhotoItemViewModel> Items { get; } = [];

    /// <summary>Gets or sets what to tell the user instead of, or above, the photos; empty for nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether <see cref="Notice"/> shows.</summary>
    public bool HasNotice => Notice.Length > 0;

    /// <summary>Gets or sets a value indicating whether older items can be loaded.</summary>
    [ObservableProperty]
    public partial bool HasMore { get; set; }

    /// <summary>Gets or sets a value indicating whether there are items.</summary>
    [ObservableProperty]
    public partial bool HasItems { get; set; }

    /// <summary>Gets or sets a value indicating whether a page is loading.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _photos.ThumbnailArrived -= OnThumbnailArrived;
        _channel.Changed -= OnChannelChanged;
    }

    /// <summary>
    /// Copies the originals of <paramref name="items"/> into <paramref name="folder"/>.
    /// </summary>
    /// <param name="items">Items.</param>
    /// <param name="folder">Target folder.</param>
    /// <param name="listed">Whether the transfer shows in the list above.</param>
    /// <returns>The saved files.</returns>
    public Task<IReadOnlyList<string>> CopyAsync(IReadOnlyList<PhotoItemViewModel> items, string folder, bool listed) => _photos.FetchAsync([.. items.Select(i => i.Id)], folder, listed);

    /// <summary>
    /// Copies one item to the incoming folder and opens it.
    /// </summary>
    /// <param name="item">Item.</param>
    /// <returns>A task that completes when it opened, or failed.</returns>
    public async Task OpenAsync(PhotoItemViewModel item)
    {
        var saved = await CopyAsync([item], _transfers.IncomingFolder, listed: true);

        if (saved.Count == 0)
        {
            Notice = _localization.GetString("Photos_FetchFailed");
            return;
        }

        await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(saved[0]));
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(fresh: true);

    [RelayCommand]
    private Task LoadMoreAsync() => LoadAsync(fresh: false);

    private async Task LoadAsync(bool fresh)
    {
        if (IsLoading)
        {
            return;
        }

        _wasAvailable = _photos.Available;

        if (!_photos.Available)
        {
            Items.Clear();
            HasItems = false;
            HasMore = false;
            Notice = _localization.GetString(!_channel.IsConnected ? "Photos_NotConnected" : !_channel.Supports(Capability.Photos) ? "Photos_Unsupported" : "Photos_NeedsWifi");
            return;
        }

        IsLoading = true;

        try
        {
            var before = fresh || Items.Count == 0 ? (DateTimeOffset?)null : Items[^1].Item.TakenAt?.ToDateTimeOffset();
            var page = await _photos.ListAsync(before, _pageSize);

            if (page is null)
            {
                Notice = _localization.GetString("Photos_NoAnswer");
                return;
            }

            if (fresh)
            {
                Items.Clear();
            }

            foreach (var item in page.Items.Where(i => Items.All(existing => existing.Id != i.Id)))
            {
                var row = new PhotoItemViewModel(item);
                Items.Add(row);
                await ShowThumbnailAsync(row);
            }

            HasItems = Items.Count > 0;
            HasMore = page.HasMore;
            Notice = page.Access switch
            {
                PhotoAccess.Denied => _localization.GetString("Photos_Denied"),
                PhotoAccess.Limited => _localization.GetString("Photos_Limited"),
                _ when Items.Count == 0 => _localization.GetString("Photos_Empty"),
                _ => string.Empty,
            };
            await _photos.RequestThumbnailsAsync(page.Items.Select(i => i.Id));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnThumbnailArrived(object? sender, string id) => _dispatcher.TryEnqueue(async () =>
    {
        if (Items.FirstOrDefault(i => i.Id == id) is { } row)
        {
            await ShowThumbnailAsync(row);
        }
    });

    private async Task ShowThumbnailAsync(PhotoItemViewModel row)
    {
        if (row.Thumbnail is not null || _photos.Thumbnail(row.Id) is not { } jpeg)
        {
            return;
        }

        var image = new BitmapImage();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(jpeg));
        stream.Seek(0);
        await image.SetSourceAsync(stream);
        row.Thumbnail = image;
    }

    // The phone came or went, or Wi‑Fi did: the list follows.
    private void OnChannelChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        if (_photos.Available != _wasAvailable)
        {
            _ = RefreshAsync();
        }
    });
}
