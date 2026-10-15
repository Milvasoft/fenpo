using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Fenpo.Spikes.S5;
using QRCoder;

// Usage: PcServer [--lan]
// Without --lan the TLS server listens on loopback only (local tests, no firewall prompt). With --lan it listens
// on all interfaces and puts the LAN addresses into the QR code, for a real phone.
Console.OutputEncoding = Encoding.UTF8;

var lan = args.Contains("--lan");
var identity = Identity.LoadOrCreate(SpikePaths.Server, Environment.MachineName);
Log.Info("main", $"PC id {identity.Id}, certificate {identity.Certificate.Subject} {identity.Fingerprint}");

var lanAddresses = NetworkInterface.GetAllNetworkInterfaces()
    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
    .Select(a => a.Address)
    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254."))
    .ToList();
Log.Info("main", $"LAN IPv4: {string.Join(", ", lanAddresses)}");

var server = new PairingServer(identity, Environment.MachineName, lan ? IPAddress.Any : IPAddress.Loopback);
server.PairingUriChanged += ShowPairingUri;
server.Start(lan ? lanAddresses : [IPAddress.Loopback]);

using var dnssd = lanAddresses.Count > 0
    ? DnsSd.Register($"{Environment.MachineName}._fenpo._tcp.local", $"{Environment.MachineName}.local", lanAddresses[0], (ushort)server.Port, new Dictionary<string, string> { ["id"] = identity.Id, ["v"] = "1" })
    : null;

Log.Info("main", "Commands: rotate (new token), q");

while (Console.ReadLine() is { } line)
{
    switch (line.Trim())
    {
        case "q":
            return 0;
        case "rotate":
            server.RotateToken();
            break;
    }
}

// Keep serving when stdin is closed (started in the background).
await Task.Delay(Timeout.Infinite);
return 0;

static void ShowPairingUri(PairingUri uri)
{
    var text = uri.ToString();
    using var generator = new QRCodeGenerator();
    using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
    var png = Path.Combine(SpikePaths.Server, "pairing-qr.png");
    File.WriteAllBytes(png, new PngByteQRCode(data).GetGraphic(8));

    Log.Info("pair", $"Pairing URI ({text.Length} chars, QR version {data.Version}): {text}");
    Log.Info("pair", $"QR image: {png}");
    Console.WriteLine(new AsciiQRCode(data).GetGraphicSmall());
}
