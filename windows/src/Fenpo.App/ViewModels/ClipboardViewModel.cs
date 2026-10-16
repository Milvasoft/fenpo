using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Clipboard page list: what was copied on either side, newest first, with "Copy" to put it back.
/// </summary>
public sealed partial class ClipboardViewModel : ObservableObject, IDisposable
{
    private readonly ClipboardSync _clipboard;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// Initializes a new instance of the <see cref="ClipboardViewModel"/> class.
    /// </summary>
    /// <param name="clipboard">Clipboard sharing.</param>
    /// <param name="localization">Strings.</param>
    public ClipboardViewModel(ClipboardSync clipboard, LocalizationService localization)
    {
        _clipboard = clipboard;
        _localization = localization;
        _clipboard.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Gets the entries.</summary>
    public ObservableCollection<ClipboardRowViewModel> Items { get; } = [];

    /// <summary>Gets or sets a value indicating whether there are entries.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasItems { get; set; }

    /// <summary>Gets a value indicating whether the empty text shows.</summary>
    public bool IsEmpty => !HasItems;

    /// <inheritdoc/>
    public void Dispose() => _clipboard.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Refresh);

    private void Refresh()
    {
        Items.Clear();

        foreach (var entry in _clipboard.History)
        {
            var caption = entry.FromPhone
                ? _localization.Format("Clipboard_FromPhone", NotificationRowViewModel.RelativeTime(entry.At, _localization))
                : _localization.GetString("Clipboard_FromPc");
            Items.Add(new ClipboardRowViewModel(entry, caption, () => _clipboard.CopyToPc(entry)));
        }

        HasItems = Items.Count > 0;
    }
}

/// <summary>
/// One clipboard entry.
/// </summary>
public sealed partial class ClipboardRowViewModel
{
    private readonly Action _copy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClipboardRowViewModel"/> class.
    /// </summary>
    /// <param name="entry">Entry.</param>
    /// <param name="caption">Where it came from and when.</param>
    /// <param name="copy">Puts it on the PC clipboard.</param>
    public ClipboardRowViewModel(ClipboardEntry entry, string caption, Action copy)
    {
        _copy = copy;
        Text = entry.Text ?? string.Empty;
        Caption = caption;
        IsImage = entry.ImagePng is not null;
        SourceGlyph = entry.FromPhone ? "" : "";

        if (entry.ImagePng is { } png)
        {
            Image = new BitmapImage();
            _ = LoadAsync(Image, png);
        }
    }

    /// <summary>Gets the text or link.</summary>
    public string Text { get; }

    /// <summary>Gets the caption ("From the phone, 1 min").</summary>
    public string Caption { get; }

    /// <summary>Gets a value indicating whether the entry is an image.</summary>
    public bool IsImage { get; }

    /// <summary>Gets a value indicating whether the entry is text.</summary>
    public bool IsText => !IsImage;

    /// <summary>Gets the image, if any.</summary>
    public BitmapImage? Image { get; }

    /// <summary>Gets the phone or PC glyph.</summary>
    public string SourceGlyph { get; }

    private static async Task LoadAsync(BitmapImage image, byte[] png)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(png));
        stream.Seek(0);
        await image.SetSourceAsync(stream);
    }

    [RelayCommand]
    private void Copy() => _copy();
}
