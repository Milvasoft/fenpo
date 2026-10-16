using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Fenpo.App.Views;

/// <summary>
/// Calls page.
/// </summary>
public sealed partial class CallsPage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CallsPage"/> class.
    /// </summary>
    public CallsPage()
    {
        ViewModel = App.GetService<CallsViewModel>();
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public CallsViewModel ViewModel { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }
}
