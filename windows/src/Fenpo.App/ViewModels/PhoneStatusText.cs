using Fenpo.App.Services;
using Fenpo.Core.Devices;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Texts that describe the phone status the same way on every surface (home page, tray, settings).
/// </summary>
public static class PhoneStatusText
{
    /// <summary>
    /// Gets the connection caption ("Bağlı", "Bağlanıyor").
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="localization">Strings.</param>
    /// <returns>The caption.</returns>
    public static string State(PhoneStatus status, LocalizationService localization) => localization.GetString(status.State switch
    {
        PhoneConnectionState.Connected => "Status_Connected",
        PhoneConnectionState.Connecting => "Status_Connecting",
        PhoneConnectionState.BrokenBond => "Status_BrokenBond",
        PhoneConnectionState.Disconnected => "Status_Disconnected",
        _ => "Status_NotPaired",
    });

    /// <summary>
    /// Gets the phone name, or "No iPhone connected".
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="localization">Strings.</param>
    /// <returns>The name.</returns>
    public static string Name(PhoneStatus status, LocalizationService localization) => string.IsNullOrEmpty(status.Name) ? localization.GetString("Home_NoPhone") : status.Name;

    /// <summary>
    /// Gets the battery level ("%78"), or a dash when unknown.
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="localization">Strings.</param>
    /// <returns>The battery text.</returns>
    public static string Battery(PhoneStatus status, LocalizationService localization) => status.BatteryPercent is { } battery ? localization.Format("Battery_Percent", battery) : "–";

    /// <summary>
    /// Gets a one-line summary for the tray: battery and links when connected ("%78, Bluetooth ve Wi‑Fi"), the state otherwise.
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="localization">Strings.</param>
    /// <returns>The summary.</returns>
    public static string Summary(PhoneStatus status, LocalizationService localization)
    {
        if (status.State != PhoneConnectionState.Connected)
        {
            return State(status, localization);
        }

        var links = localization.GetString((status.WifiConnected, status.UsbConnected) switch
        {
            (true, _) => "Tray_LinksBluetoothWifi",
            (false, true) => "Tray_LinksBluetoothUsb",
            _ => "Tray_LinksBluetooth",
        });

        return status.BatteryPercent is null ? links : localization.Format("Tray_Summary", Battery(status, localization), links);
    }
}
