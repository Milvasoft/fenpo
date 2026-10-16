using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Controls;

/// <summary>
/// One line of the pairing wizard's live checklist: done (green check), waiting (spinner) or not yet (empty circle).
/// </summary>
public sealed partial class CheckRow : UserControl
{
    /// <summary>Identifies <see cref="IsOk"/>.</summary>
    public static readonly DependencyProperty IsOkProperty = DependencyProperty.Register(nameof(IsOk), typeof(bool), typeof(CheckRow), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsWaiting"/>.</summary>
    public static readonly DependencyProperty IsWaitingProperty = DependencyProperty.Register(nameof(IsWaiting), typeof(bool), typeof(CheckRow), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="Title"/>.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(CheckRow), new PropertyMetadata(string.Empty));

    /// <summary>Identifies <see cref="Hint"/>.</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(CheckRow), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckRow"/> class.
    /// </summary>
    public CheckRow()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets a value indicating whether the check passed.</summary>
    public bool IsOk
    {
        get => (bool)GetValue(IsOkProperty);
        set => SetValue(IsOkProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether this is the check being waited for.</summary>
    public bool IsWaiting
    {
        get => (bool)GetValue(IsWaitingProperty);
        set => SetValue(IsWaitingProperty, value);
    }

    /// <summary>Gets or sets the title.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the hint.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}
