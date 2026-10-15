/// A PC this phone is paired with.
public struct PairedPC: Equatable, Sendable, Identifiable {
    /// Stable PC id from the pairing result.
    public let id: String

    /// User visible name, e.g. "BUGRA-PC".
    public var name: String

    /// Operating system shown under the name, e.g. "Windows 11".
    public var systemName: String

    /// Creates a paired PC.
    /// - Parameters:
    ///   - id: Stable PC id.
    ///   - name: User visible name.
    ///   - systemName: Operating system shown under the name.
    public init(id: String, name: String, systemName: String) {
        self.id = id
        self.name = name
        self.systemName = systemName
    }
}

/// How close the PC is, from the BLE signal strength.
public enum Proximity: Equatable, Sendable {
    /// Within a few meters, e.g. on the same desk.
    case near

    /// Connected but far away.
    case away

    /// No reading yet.
    case unknown
}

/// Which of the connection paths to the PC are up.
public struct ChannelLinks: Equatable, Sendable {
    /// Fenpo BLE channel.
    public var bluetooth: Bool

    /// Wi-Fi TLS data channel.
    public var wifi: Bool

    /// USB data channel.
    public var usb: Bool

    /// Creates a link set.
    /// - Parameters:
    ///   - bluetooth: Fenpo BLE channel is connected.
    ///   - wifi: Wi-Fi data channel is connected.
    ///   - usb: USB data channel is connected.
    public init(bluetooth: Bool, wifi: Bool, usb: Bool) {
        self.bluetooth = bluetooth
        self.wifi = wifi
        self.usb = usb
    }

    /// No path is up.
    public static let none = ChannelLinks(bluetooth: false, wifi: false, usb: false)

    /// At least one path is up.
    public var isConnected: Bool {
        bluetooth || wifi || usb
    }
}
