import Foundation

/// What the widgets show, written by the app to the App Group whenever it changes.
public struct SharedStatus: Codable, Equatable, Sendable {
    /// Paired PC name, or `nil` before pairing.
    public var pcName: String?

    /// The BLE channel is up.
    public var isConnected: Bool

    /// PC battery from its `DeviceState`, when the PC has a battery.
    public var pcBatteryPercent: Int?

    /// The PC is charging.
    public var pcCharging: Bool?

    /// Name of the last transferred file.
    public var lastTransferName: String?

    /// When the app wrote this.
    public var updatedAt: Date

    /// Creates a status.
    public init(
        pcName: String?,
        isConnected: Bool,
        pcBatteryPercent: Int? = nil,
        pcCharging: Bool? = nil,
        lastTransferName: String? = nil,
        updatedAt: Date = Date()
    ) {
        self.pcName = pcName
        self.isConnected = isConnected
        self.pcBatteryPercent = pcBatteryPercent
        self.pcCharging = pcCharging
        self.lastTransferName = lastTransferName
        self.updatedAt = updatedAt
    }

    /// Nothing paired.
    public static let unpaired = SharedStatus(pcName: nil, isConnected: false)

    /// The state in design/mobile/mobile.html, for widget previews and the gallery, with the
    /// sample session's demo names.
    public static var sample: SharedStatus {
        let demo = SampleSession.pairedSnapshot()
        return SharedStatus(
            pcName: demo.pc?.name,
            isConnected: true,
            pcBatteryPercent: 64,
            pcCharging: false,
            lastTransferName: demo.transfers.first?.fileName
        )
    }
}
