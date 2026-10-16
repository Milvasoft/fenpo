using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Fenpo.App.Views;

/// <summary>
/// Clipboard page.
/// </summary>
public sealed partial class ClipboardPage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClipboardPage"/> class.
    /// </summary>
    public ClipboardPage()
    {
        ViewModel = App.GetService<AppFeatureViewModel>();
        Clipboard = App.GetService<ClipboardViewModel>();
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model (is the phone app connected, sharing switch).
    /// </summary>
    public AppFeatureViewModel ViewModel { get; }

    /// <summary>
    /// Gets the list view model.
    /// </summary>
    public ClipboardViewModel Clipboard { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
        Clipboard.Dispose();
    }
}
