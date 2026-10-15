using Windows.Devices.Enumeration;

namespace Fenpo.Spikes.S1;

/// <summary>
/// Lists nearby Bluetooth devices and pairs one from inside the app, to see whether Fenpo's setup wizard
/// can pair the iPhone itself instead of sending the user to Windows Settings.
/// </summary>
internal static class Pairing
{
    private const string _leProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private const string _classicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";

    public static async Task ScanAsync()
    {
        var filter = $"(System.Devices.Aep.ProtocolId:=\"{_leProtocol}\" OR System.Devices.Aep.ProtocolId:=\"{_classicProtocol}\")";
        string[] properties = ["System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsPaired", "System.Devices.Aep.CanPair", "System.Devices.Aep.SignalStrength", "System.Devices.Aep.ProtocolId"];
        var watcher = DeviceInformation.CreateWatcher(filter, properties, DeviceInformationKind.AssociationEndpoint);

        watcher.Added += (_, info) =>
        {
            if (string.IsNullOrWhiteSpace(info.Name))
            {
                return;
            }

            var protocol = info.Properties.TryGetValue("System.Devices.Aep.ProtocolId", out var p) && p?.ToString()?.Contains("bb7bb05e", StringComparison.OrdinalIgnoreCase) == true ? "LE" : "classic";
            info.Properties.TryGetValue("System.Devices.Aep.SignalStrength", out var rssi);
            Log.Info("scan", $"{info.Name} [{protocol}] paired={info.Pairing.IsPaired} canPair={info.Pairing.CanPair} rssi={rssi ?? "-"}");
            Log.Info("scan", $"  id: {info.Id}");
        };
        watcher.Updated += (_, _) => { };
        watcher.EnumerationCompleted += (_, _) => Log.Info("scan", "Enumeration completed, still listening. Press Enter to stop.");
        watcher.Start();

        Log.Info("scan", "Scanning. Keep the iPhone on Settings > Bluetooth so it stays discoverable. Press Enter to stop.");
        await Task.Run(Console.ReadLine);
        watcher.Stop();
    }

    public static async Task PairAsync(string deviceId)
    {
        var info = await DeviceInformation.CreateFromIdAsync(deviceId);
        Log.Info("pair", $"{info.Name} paired={info.Pairing.IsPaired} canPair={info.Pairing.CanPair} protection={info.Pairing.ProtectionLevel}");

        if (info.Pairing.IsPaired)
        {
            Log.Warn("pair", "Already paired. Remove it in Windows Settings first to test fresh pairing.");
            return;
        }

        var custom = info.Pairing.Custom;

        custom.PairingRequested += (_, args) =>
        {
            Log.Info("pair", $"Pairing requested: kind={args.PairingKind} pin={args.Pin}");

            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                    args.Accept();
                    break;
                case DevicePairingKinds.ConfirmPinMatch:
                case DevicePairingKinds.DisplayPin:
                    Log.Info("pair", $"Does the iPhone show {args.Pin}? Type y and Enter to accept, anything else to reject.");
                    var deferral = args.GetDeferral();

                    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        args.Accept();
                    }

                    deferral.Complete();
                    break;
                case DevicePairingKinds.ProvidePin:
                    Log.Info("pair", "Type the PIN shown on the iPhone and press Enter.");
                    var pinDeferral = args.GetDeferral();
                    args.Accept(Console.ReadLine()?.Trim() ?? string.Empty);
                    pinDeferral.Complete();
                    break;
            }
        };

        var kinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin;
        var result = await custom.PairAsync(kinds, DevicePairingProtectionLevel.EncryptionAndAuthentication);
        Log.Info("pair", $"Result: {result.Status}, protection={result.ProtectionLevelUsed}");
    }
}
