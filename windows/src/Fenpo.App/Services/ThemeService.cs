using Fenpo.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Fenpo.App.Services;

/// <summary>
/// Applies the Theme setting (System, Light, Dark) to every Fenpo window: the main window, the tray flyout and the island
/// all register here. WinUI cannot change Application.RequestedTheme at run time, so the root element's theme is set instead.
/// </summary>
public sealed class ThemeService(ISettingsStore settings, ILogger<ThemeService> logger)
{
    private readonly ISettingsStore _settings = settings;
    private readonly ILogger<ThemeService> _logger = logger;
    private readonly UISettings _uiSettings = new();
    private readonly List<Window> _windows = [];
    private bool _listening;

    /// <summary>
    /// Raised after the effective theme changed.
    /// </summary>
    public event EventHandler<ElementTheme>? ThemeChanged;

    /// <summary>
    /// Gets the theme currently in effect (Light or Dark, never Default).
    /// </summary>
    public ElementTheme EffectiveTheme => _settings.Current.Theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => IsSystemDark() ? ElementTheme.Dark : ElementTheme.Light,
    };

    /// <summary>
    /// Starts applying the theme to <paramref name="window"/> until it closes.
    /// </summary>
    /// <param name="window">A Fenpo window whose content is already set.</param>
    public void Register(Window window)
    {
        if (!_listening)
        {
            _listening = true;
            _settings.Changed += (_, _) => ApplyAll();
            _uiSettings.ColorValuesChanged += (_, _) =>
            {
                if (_settings.Current.Theme == AppTheme.System)
                {
                    ApplyAll();
                }
            };
        }

        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(window);
    }

    private void ApplyAll()
    {
        foreach (var window in _windows.ToArray())
        {
            window.DispatcherQueue.TryEnqueue(() => Apply(window));
        }

        ThemeChanged?.Invoke(this, EffectiveTheme);
    }

    private void Apply(Window window)
    {
        var theme = EffectiveTheme;

        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        // Caption buttons are drawn by the system and do not follow RequestedTheme.
        var titleBar = window.AppWindow.TitleBar;
        var foreground = theme == ElementTheme.Dark ? Colors.White : Windows.UI.Color.FromArgb(0xFF, 0x1B, 0x1F, 0x3B);
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = theme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x0D, 0x1B, 0x1F, 0x3B);
        _logger.LogDebug("Applied {Theme} theme to {Window}", theme, window.GetType().Name);
    }

    private bool IsSystemDark() => _uiSettings.GetColorValue(UIColorType.Background) == Colors.Black;
}
