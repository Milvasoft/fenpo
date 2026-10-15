namespace Fenpo.Core.Settings;

/// <summary>
/// Holds the current settings and saves every change.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Raised after <see cref="Current"/> changed.
    /// </summary>
    event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Gets the current settings.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Applies <paramref name="change"/> to the current settings and saves the result.
    /// </summary>
    /// <param name="change">Produces the new settings from the current ones.</param>
    void Update(Func<AppSettings, AppSettings> change);
}
