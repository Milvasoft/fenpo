using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Views;

/// <summary>
/// First-run pairing wizard shown over the main window's content.
/// </summary>
public sealed partial class PairingView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PairingView"/> class.
    /// </summary>
    /// <param name="viewModel">View model.</param>
    public PairingView(PairingViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public PairingViewModel ViewModel { get; }
}
