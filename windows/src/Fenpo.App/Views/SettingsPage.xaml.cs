using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

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
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public SettingsViewModel ViewModel { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private void OnPairClick(object sender, RoutedEventArgs e) => App.GetService<MainWindow>().ShowPairing();

    private async void OnManagePairingClick(object sender, RoutedEventArgs e) => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));

    private async void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.GetService<MainWindow>()));

        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            ViewModel.SetIncomingFolder(folder.Path);
        }
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e) => ViewModel.ThemeIndex = ReferenceEquals(sender, ThemeLight) ? 1 : ReferenceEquals(sender, ThemeDark) ? 2 : 0;
}
