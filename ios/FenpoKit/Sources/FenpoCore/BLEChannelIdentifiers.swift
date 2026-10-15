/// GATT identifiers of the Fenpo BLE channel (protocol/README.md).
///
/// The PC is the GATT server and the phone the client. The values are the same for
/// every PC because AccessorySetupKit needs the service UUID in the app's Info.plist.
public enum BLEChannelIdentifiers {
    /// Primary Fenpo service, advertised by the PC.
    public static let service = "1888047E-C8C5-45CC-AAEA-333A8859E79F"

    /// Phone to PC. Write without response, encryption required.
    public static let rx = "ECC8D0D1-990A-4B02-A4FC-49FA647E7254"

    /// PC to phone. Notify, encryption required.
    public static let tx = "E9EC01E0-8C02-4B8E-902C-4F113E25DFC8"
}
