using Fenpo.App.ViewModels;
using Fenpo.Protocol.V1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace Fenpo.App.Demo;

/// <summary>
/// The demo phone's recent photos: color washes in the brand colors in place of pictures.
/// </summary>
public static class DemoPhotos
{
    private const int _count = 14;
    private const int _size = 160;
    private static readonly string[] _colorKeys = ["FenpoBloom", "FenpoDusk", "FenpoLeaf", "FenpoGlass", "FenpoInk"];

    /// <summary>
    /// Creates the photo tiles with their thumbnails.
    /// </summary>
    /// <returns>The tiles, newest first.</returns>
    public static async Task<IReadOnlyList<PhotoItemViewModel>> CreateAsync()
    {
        var colors = _colorKeys.Select(key => (Color)Application.Current.Resources[key]).ToArray();
        var items = new List<PhotoItemViewModel>();

        for (var index = 0; index < _count; index++)
        {
            var video = index % 6 == 2;
            var item = new PhotoItemViewModel(new PhotoItem { Id = $"demo-{index}", Name = $"IMG_{4120 - index}.{(video ? "MOV" : "HEIC")}", IsVideo = video })
            {
                Thumbnail = await WashAsync(colors[index % colors.Length], colors[((index * 2) + 1) % colors.Length], mirrored: index % 2 == 1),
            };
            items.Add(item);
        }

        return items;
    }

    // A diagonal blend from one color to another.
    private static async Task<BitmapImage> WashAsync(Color from, Color to, bool mirrored)
    {
        var pixels = new byte[_size * _size * 4];

        for (var y = 0; y < _size; y++)
        {
            for (var x = 0; x < _size; x++)
            {
                var t = ((mirrored ? _size - 1 - x : x) + y) / (2.0 * (_size - 1));
                var offset = ((y * _size) + x) * 4;
                pixels[offset] = (byte)(from.B + ((to.B - from.B) * t));
                pixels[offset + 1] = (byte)(from.G + ((to.G - from.G) * t));
                pixels[offset + 2] = (byte)(from.R + ((to.R - from.R) * t));
                pixels[offset + 3] = 255;
            }
        }

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, _size, _size, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }
}
