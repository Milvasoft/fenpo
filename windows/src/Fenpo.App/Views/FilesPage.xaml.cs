using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public AppFeatureViewModel ViewModel { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }
}
