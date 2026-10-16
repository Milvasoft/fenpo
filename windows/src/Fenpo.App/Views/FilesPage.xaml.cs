using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Fenpo.App.Views;

/// <summary>
/// Files and photos page.
/// </summary>
public sealed partial class FilesPage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FilesPage"/> class.
    /// </summary>
    public FilesPage()
    {
        ViewModel = App.GetService<AppFeatureViewModel>();
        Files = App.GetService<FilesViewModel>();
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model (is the phone app connected).
    /// </summary>
    public AppFeatureViewModel ViewModel { get; }

    /// <summary>
    /// Gets the transfers view model.
    /// </summary>
    public FilesViewModel Files { get; }

    /// <summary>
    /// Lets the user pick files and sends them to the phone.
    /// </summary>
    /// <returns>A task that completes when the offer went out or the picker was closed.</returns>
    public async Task PickAndSendAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.GetService<MainWindow>()));
        var files = await picker.PickMultipleFilesAsync();

        if (files.Count > 0)
        {
            await Files.SendAsync([.. files.Select(f => f.Path)]);
        }
    }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
        Files.Dispose();
    }

    private void OnPickClick(object sender, RoutedEventArgs e) => _ = PickAndSendAsync();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (ViewModel.AppConnected && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        await Files.SendAsync([.. items.OfType<StorageFile>().Select(f => f.Path)]);
    }
}
