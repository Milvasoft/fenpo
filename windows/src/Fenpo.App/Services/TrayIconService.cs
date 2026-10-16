using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace Fenpo.App.Services;

/// <summary>
/// Fenpo's icon in the notification area: left click opens the tray panel, the context menu opens the main window or
/// quits, the tooltip shows the phone summary.
/// </summary>
public sealed class TrayIconService(TrayViewModel viewModel, LocalizationService localization) : IDisposable
{
    private readonly TrayViewModel _viewModel = viewModel;
    private readonly LocalizationService _localization = localization;
    private TaskbarIcon? _icon;

    /// <summary>
    /// Raised on a left click on the icon.
    /// </summary>
    public event EventHandler? PanelRequested;

    /// <summary>
    /// Raised when the user picks "Open Fenpo".
    /// </summary>
    public event EventHandler? OpenRequested;

    /// <summary>
    /// Raised when the user picks "Quit".
    /// </summary>
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Creates the icon. Call once on the UI thread.
    /// </summary>
    public void Create()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = _localization.GetString("Tray_MenuOpen"), Command = new RelayCommand(() => OpenRequested?.Invoke(this, EventArgs.Empty)) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = _localization.GetString("Tray_MenuQuit"), Command = new RelayCommand(() => QuitRequested?.Invoke(this, EventArgs.Empty)) });

        _icon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "Fenpo.ico")),
            ToolTipText = _viewModel.ToolTip,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => PanelRequested?.Invoke(this, EventArgs.Empty)),
            ContextFlyout = menu,
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _icon?.Dispose();
        _icon = null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayViewModel.ToolTip) && _icon is not null)
        {
            _icon.ToolTipText = _viewModel.ToolTip;
        }
    }
}
