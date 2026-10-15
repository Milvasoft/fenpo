using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Controls;

/// <summary>
/// A numbered step of the pairing wizard: pending (outline), current (accent) or done (green check).
/// </summary>
public sealed partial class WizardStep : UserControl
{
    /// <summary>Identifies <see cref="Number"/>.</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(nameof(Number), typeof(string), typeof(WizardStep), new PropertyMetadata("1"));

    /// <summary>Identifies <see cref="State"/>.</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(StepState), typeof(WizardStep), new PropertyMetadata(StepState.Pending));

    /// <summary>Identifies <see cref="Title"/>.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(WizardStep), new PropertyMetadata(string.Empty));

    /// <summary>Identifies <see cref="Hint"/>.</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(WizardStep), new PropertyMetadata(string.Empty));

    /// <summary>Identifies <see cref="Extra"/>.</summary>
    public static readonly DependencyProperty ExtraProperty = DependencyProperty.Register(nameof(Extra), typeof(object), typeof(WizardStep), new PropertyMetadata(null));

    /// <summary>
    /// Initializes a new instance of the <see cref="WizardStep"/> class.
    /// </summary>
    public WizardStep()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets the step number shown in the circle.</summary>
    public string Number
    {
        get => (string)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    /// <summary>Gets or sets the state.</summary>
    public StepState State
    {
        get => (StepState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Gets or sets the title.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the hint under the title.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>Gets or sets extra content under the hint (a button).</summary>
    public object? Extra
    {
        get => GetValue(ExtraProperty);
        set => SetValue(ExtraProperty, value);
    }
}
