using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Fenpo.App.Views;

/// <summary>
/// "My iPhone" page.
/// </summary>
public sealed partial class HomePage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HomePage"/> class.
    /// </summary>
    public HomePage()
    {
        ViewModel = App.GetService<HomeViewModel>();
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public HomeViewModel ViewModel { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private void OnSeeAllClick(object sender, RoutedEventArgs e) => App.GetService<MainWindow>().Navigate("Notifications");

    private void OnFilesClick(object sender, RoutedEventArgs e) => App.GetService<MainWindow>().Navigate("Files");

    private void OnSendFileClick(object sender, RoutedEventArgs e) => _ = App.GetService<MainWindow>().SendFilesAsync();

    private void OnPairClick(object sender, RoutedEventArgs e) => App.GetService<MainWindow>().ShowPairing();
}
