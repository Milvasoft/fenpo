using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Fenpo.App.Controls;

/// <summary>
/// Lays children out left to right and starts a new line when the next one does not fit (status chips, pills).
/// </summary>
public sealed partial class WrapPanel : Panel
{
    /// <summary>Identifies <see cref="Spacing"/>.</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Identifies <see cref="LineSpacing"/>.</summary>
    public static readonly DependencyProperty LineSpacingProperty = DependencyProperty.Register(nameof(LineSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>
    /// Gets or sets the horizontal space between children.
    /// </summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical space between lines.
    /// </summary>
    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        return Arrange(availableSize.Width, arrange: false);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Arrange(finalSize.Width, arrange: true);
        return finalSize;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WrapPanel)d).InvalidateMeasure();

    private Size Arrange(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, widest = 0;

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += lineHeight + LineSpacing;
                lineHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x += size.Width + Spacing;
            widest = Math.Max(widest, x - Spacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(widest, y + lineHeight);
    }
}
