using Fenpo.App.ViewModels;
using Microsoft.UI.Xaml;

namespace Fenpo.App.Helpers;

/// <summary>
/// Functions for x:Bind function bindings.
/// </summary>
public static class BindHelpers
{
    /// <summary>
    /// Negates a boolean.
    /// </summary>
    /// <param name="value">Value.</param>
    /// <returns>The opposite.</returns>
    public static bool Not(bool value) => !value;

    /// <summary>
    /// Visible when false.
    /// </summary>
    /// <param name="value">Value.</param>
    /// <returns>Collapsed when true, Visible when false.</returns>
    public static Visibility CollapsedWhen(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Visible when the text is not empty.
    /// </summary>
    /// <param name="value">Text.</param>
    /// <returns>Visible or Collapsed.</returns>
    public static Visibility VisibleWhenText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Opacity for a status dot: full when on, faded when off.
    /// </summary>
    /// <param name="on">State.</param>
    /// <returns>Opacity.</returns>
    public static double DimWhenOff(bool on) => on ? 1.0 : 0.45;

    /// <summary>
    /// Visible when a wizard step is done. Step circles are three overlaid shapes so each can use theme resources.
    /// </summary>
    /// <param name="state">Step state.</param>
    /// <returns>Visible or Collapsed.</returns>
    public static Visibility IfDone(StepState state) => state == StepState.Done ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Visible when a wizard step is the current one.
    /// </summary>
    /// <param name="state">Step state.</param>
    /// <returns>Visible or Collapsed.</returns>
    public static Visibility IfCurrent(StepState state) => state == StepState.Current ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Visible when a wizard step is still ahead.
    /// </summary>
    /// <param name="state">Step state.</param>
    /// <returns>Visible or Collapsed.</returns>
    public static Visibility IfPending(StepState state) => state == StepState.Pending ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// True when the previous check passed and this one has not yet: the one to wait for.
    /// </summary>
    /// <param name="previousOk">Previous check.</param>
    /// <param name="thisOk">This check.</param>
    /// <returns>True while waiting for this check.</returns>
    public static bool WaitingFor(bool previousOk, bool thisOk) => previousOk && !thisOk;
}
