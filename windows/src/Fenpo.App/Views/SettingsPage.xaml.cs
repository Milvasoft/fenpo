using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Views;

/// <summary>
/// Settings page.
/// </summary>
public sealed partial class SettingsPage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPage"/> class.
    /// </summary>
    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();

        RadioButton[] themes = [ThemeSystem, ThemeLight, ThemeDark];
        themes[ViewModel.ThemeIndex].IsChecked = true;
        IncomingFolderText.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Fenpo");
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public SettingsViewModel ViewModel { get; }

    private void OnThemeChecked(object sender, RoutedEventArgs e) => ViewModel.ThemeIndex = ReferenceEquals(sender, ThemeLight) ? 1 : ReferenceEquals(sender, ThemeDark) ? 2 : 0;
}
