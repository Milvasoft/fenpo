using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Fenpo.App.Views;

/// <summary>
/// Notifications page.
/// </summary>
public sealed partial class NotificationsPage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationsPage"/> class.
    /// </summary>
    public NotificationsPage()
    {
        ViewModel = App.GetService<NotificationsViewModel>();
        InitializeComponent();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public NotificationsViewModel ViewModel { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private void OnFilterChecked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.FilterIndex = ReferenceEquals(sender, FilterMessages) ? 1 : ReferenceEquals(sender, FilterCalls) ? 2 : ReferenceEquals(sender, FilterOther) ? 3 : 0;
    }

    private void OnDismissClick(object sender, RoutedEventArgs e) => ViewModel.DismissCommand.Execute((string)((FrameworkElement)sender).Tag);

    private void OnPositiveClick(object sender, RoutedEventArgs e) => ViewModel.PositiveCommand.Execute((string)((FrameworkElement)sender).Tag);
}
