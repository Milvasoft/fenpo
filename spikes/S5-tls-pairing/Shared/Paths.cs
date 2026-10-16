namespace Fenpo.Spikes.S5;

/// <summary>
/// Where the spike keeps identities and state: %LOCALAPPDATA%\Fenpo\Spikes\S5.
/// </summary>
public static class SpikePaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fenpo", "Spikes", "S5");

    public static string Server => Path.Combine(Root, "pc");

    public static string Phone => Path.Combine(Root, "phone");

    /// <summary>
    /// The PC writes its current pairing URI here so the phone simulator can pick it up instead of scanning a QR code.
    /// </summary>
    public static string CurrentPairingUri => Path.Combine(Server, "pairing-uri.txt");
}
