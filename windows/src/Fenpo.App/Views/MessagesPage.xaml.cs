using Fenpo.App.Services;
using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Fenpo.App.Views;

/// <summary>
/// Messages page.
/// </summary>
public sealed partial class MessagesPage : Page
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessagesPage"/> class.
    /// </summary>
    public MessagesPage()
    {
        ViewModel = App.GetService<MessagesViewModel>();
        InitializeComponent();
        ViewModel.Bubbles.CollectionChanged += (sender, e) => DispatcherQueue.TryEnqueue(() => ThreadScroller.ChangeView(null, ThreadScroller.ScrollableHeight, null, disableAnimation: true));
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public MessagesViewModel ViewModel { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    private void OnDraftKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter sends, like the phone's keyboard; Shift+Enter is left to the text box.
        if (e.Key == VirtualKey.Enter && ViewModel.SendCommand.CanExecute(null))
        {
            e.Handled = true;
            ViewModel.SendCommand.Execute(null);
        }
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        var localization = App.GetService<LocalizationService>();
        var number = new TextBox { Header = localization.GetString("Messages_NewNumber"), InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.TelephoneNumber) } } };
        var text = new TextBox { Header = localization.GetString("Messages_NewText"), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 80 };
        var error = new TextBlock { Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FenpoErrorBrush"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = localization.GetString("Messages_NewTitle"),
            Content = new StackPanel { Spacing = 12, MinWidth = 340, Children = { number, text, error } },
            PrimaryButtonText = localization.GetString("Messages_NewSend"),
            CloseButtonText = localization.GetString("Messages_NewCancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        void Validate(object? s, object? a) => dialog.IsPrimaryButtonEnabled = MessagesViewModel.IsDialable(number.Text) && !string.IsNullOrWhiteSpace(text.Text);
        number.TextChanged += (s, a) => Validate(s, a);
        text.TextChanged += (s, a) => Validate(s, a);

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            var deferral = args.GetDeferral();

            if (!await ViewModel.SendNewAsync(number.Text, text.Text))
            {
                args.Cancel = true;
                error.Text = localization.GetString("Messages_SendFailed");
                error.Visibility = Visibility.Visible;
            }

            deferral.Complete();
        };

        await dialog.ShowAsync();
    }
}
