namespace Fenpo.Core.Settings;

/// <summary>
/// Appearance choice shared by the main window, the tray flyout and the island.
/// </summary>
public enum AppTheme
{
    /// <summary>
    /// Follow the Windows light or dark setting.
    /// </summary>
    System,

    /// <summary>
    /// Always light.
    /// </summary>
    Light,

    /// <summary>
    /// Always dark.
    /// </summary>
    Dark,
}

/// <summary>
/// User settings, persisted as JSON in the package's local folder.
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Language tag used when <see cref="Language"/> follows the system.
    /// </summary>
    public const string SystemLanguage = "system";

    /// <summary>
    /// Gets the appearance.
    /// </summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>
    /// Gets the user interface language: <see cref="SystemLanguage"/>, "tr-TR" or "en-US".
    /// </summary>
    public string Language { get; init; } = SystemLanguage;

    /// <summary>
    /// Gets a value indicating whether iPhone notifications appear as Windows notifications.
    /// </summary>
    public bool ShowNotifications { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether incoming calls appear in the island.
    /// </summary>
    public bool ShowIncomingCalls { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the Windows clipboard is shared with the phone.
    /// </summary>
    public bool ShareClipboard { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether keyboard media keys control the phone.
    /// </summary>
    public bool MediaControl { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the PC locks when the phone moves away.
    /// </summary>
    public bool ProximityLock { get; init; } = true;

    /// <summary>
    /// Gets the averaged signal strength below which the phone counts as away.
    /// </summary>
    public int ProximityThresholdDbm { get; init; } = -80;

    /// <summary>
    /// Gets how long the phone has to stay away before the PC locks.
    /// </summary>
    public int ProximityDelaySeconds { get; init; } = 30;

    /// <summary>
    /// Gets a value indicating whether Fenpo notifications go quiet while the phone is in a Focus mode.
    /// </summary>
    public bool FocusSync { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether incoming calls pause media playing on the PC.
    /// </summary>
    public bool PauseMediaDuringCalls { get; init; } = true;

    /// <summary>
    /// Gets the folder for files received from the phone, or null for Downloads\Fenpo.
    /// </summary>
    public string? IncomingFolder { get; init; }

    /// <summary>
    /// Gets the Bluetooth LE device id of the phone Fenpo uses, or null to pick the paired iPhone by itself.
    /// </summary>
    public string? PhoneDeviceId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the first-run pairing wizard has been completed or skipped.
    /// </summary>
    public bool SetupCompleted { get; init; }
}
