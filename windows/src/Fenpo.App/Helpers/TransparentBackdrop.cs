using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Fenpo.App.Helpers;

/// <summary>
/// A fully transparent window background, so XAML can draw a shape with smooth (anti-aliased) edges instead of
/// clipping the window with a region, which left jagged edges. Used by the island.
/// </summary>
public sealed partial class TransparentBackdrop : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? _compositor;

    /// <inheritdoc/>
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        // The backdrop brush comes from the system compositor, which needs a Windows.System dispatcher queue here.
        NativeMethods.EnsureSystemDispatcherQueue();
        _compositor ??= new Windows.UI.Composition.Compositor();
        connectedTarget.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    /// <inheritdoc/>
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }
}
