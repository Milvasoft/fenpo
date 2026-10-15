using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Controls;

/// <summary>
/// Banner above a feature that needs Fenpo on the iPhone, with a button to connect the iPhone app.
/// </summary>
public sealed partial class NeedsAppBanner : UserControl
{
    /// <summary>Identifies <see cref="Message"/>.</summary>
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(NeedsAppBanner), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initializes a new instance of the <see cref="NeedsAppBanner"/> class.
    /// </summary>
    public NeedsAppBanner()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets what the feature needs the app for.
    /// </summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    private void OnConnectClick(object sender, RoutedEventArgs e) => App.GetService<MainWindow>().ShowPairing();
}
