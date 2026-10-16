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
        Sharing = App.GetService<SharingSettingsViewModel>();
        InitializeComponent();

        RadioButton[] themes = [ThemeSystem, ThemeLight, ThemeDark];
        themes[ViewModel.ThemeIndex].IsChecked = true;
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// Gets the sharing section's view model.
    /// </summary>
    public SharingSettingsViewModel Sharing { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
        Sharing.Dispose();
    }

    private void OnPairClick(object sender, RoutedEventArgs e) => App.GetService<MainWindow>().ShowPairing();

    private void OnConnectAppClick(object sender, RoutedEventArgs e) => App.GetService<MainWindow>().ShowAppPairing();

    // "Active" is the phone in use, "Other" the second paired phone.
    private async void OnForgetClick(object sender, RoutedEventArgs e)
    {
        var status = App.GetService<Fenpo.Core.Devices.IPhoneStatusSource>();
        var kind = sender is FrameworkElement { Tag: "Other" }
            ? status.Active == Fenpo.Core.Devices.PhoneKind.Iphone ? Fenpo.Core.Devices.PhoneKind.Android : Fenpo.Core.Devices.PhoneKind.Iphone
            : status.Active;
        var localization = App.GetService<Services.LocalizationService>();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localization.Format("Forget_Title", PhoneStatusText.Name(status.StatusOf(kind), localization)),
            Content = localization.GetString(kind == Fenpo.Core.Devices.PhoneKind.Iphone ? "Forget_TextIphone" : "Forget_Text"),
            PrimaryButtonText = localization.GetString("Forget_Confirm"),
            CloseButtonText = localization.GetString("Forget_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            // The iPhone's Bluetooth pairing goes too; the Android phone's is part of Fenpo's own record.
            if (kind == Fenpo.Core.Devices.PhoneKind.Iphone)
            {
                await App.GetService<Fenpo.Bluetooth.BluetoothHostedService>().ForgetIphoneAsync();
            }

            await App.GetService<Services.PhoneChannel>().ForgetAsync(kind);
        }
    }

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

    private async void OnAddSharedFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.GetService<MainWindow>()));

        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            Sharing.AddFolder(folder.Path);
        }
    }

    private void OnRemoveSharedFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SharedFolderItem folder })
        {
            Sharing.RemoveFolderCommand.Execute(folder);
        }
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e) => ViewModel.ThemeIndex = ReferenceEquals(sender, ThemeLight) ? 1 : ReferenceEquals(sender, ThemeDark) ? 2 : 0;
}
