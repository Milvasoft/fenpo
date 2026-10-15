using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Fenpo.App.Views;

/// <summary>
/// Camera page.
/// </summary>
public sealed partial class CameraPage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CameraPage"/> class.
    /// </summary>
    public CameraPage()
    {
        ViewModel = App.GetService<AppFeatureViewModel>();
        InitializeComponent();
        (ViewModel.LensIndex switch
        {
            1 => LensUltraWide,
            2 => LensFront,
            _ => LensWide,
        }).IsChecked = true;
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

    private void OnLensChecked(object sender, RoutedEventArgs e) => ViewModel.LensIndex = ReferenceEquals(sender, LensUltraWide) ? 1 : ReferenceEquals(sender, LensFront) ? 2 : 0;
}
