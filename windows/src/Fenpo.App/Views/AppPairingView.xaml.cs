using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Views;

/// <summary>
/// Pairing with the Fenpo iPhone app through the QR code, shown over the main window's content.
/// </summary>
public sealed partial class AppPairingView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppPairingView"/> class.
    /// </summary>
    /// <param name="viewModel">View model.</param>
    public AppPairingView(AppPairingViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public AppPairingViewModel ViewModel { get; }
}
