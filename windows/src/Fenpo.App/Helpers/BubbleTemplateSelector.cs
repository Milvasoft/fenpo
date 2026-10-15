using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Helpers;

/// <summary>
/// Picks the template for a conversation line: day separator, received or sent bubble.
/// </summary>
public sealed partial class BubbleTemplateSelector : DataTemplateSelector
{
    /// <summary>Gets or sets the day separator template.</summary>
    public DataTemplate? Day { get; set; }

    /// <summary>Gets or sets the received bubble template.</summary>
    public DataTemplate? Incoming { get; set; }

    /// <summary>Gets or sets the sent bubble template.</summary>
    public DataTemplate? Outgoing { get; set; }

    /// <inheritdoc/>
    protected override DataTemplate? SelectTemplateCore(object item) => (item as BubbleItem)?.Kind switch
    {
        BubbleKind.Day => Day,
        BubbleKind.Outgoing => Outgoing,
        _ => Incoming,
    };

    /// <inheritdoc/>
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
