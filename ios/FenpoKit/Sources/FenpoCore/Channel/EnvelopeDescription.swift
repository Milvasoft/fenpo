import FenpoProtocol

extension Fenpo_V1_Envelope {
    /// Message type for logs, e.g. "Ping". Never includes the contents.
    public var bodyName: String {
        switch body {
        case .hello:
            "Hello"
        case .ping:
            "Ping"
        case .pong:
            "Pong"
        case .error:
            "Error"
        case .pairingRequest:
            "PairingRequest"
        case .pairingResult:
            "PairingResult"
        case .clipboard:
            "ClipboardItem"
        case .clipboardRequest:
            "ClipboardRequest"
        case .openURL:
            "OpenUrl"
        case .transferOffer:
            "TransferOffer"
        case .transferAccept:
            "TransferAccept"
        case .transferProgress:
            "TransferProgress"
        case .transferCancel:
            "TransferCancel"
        case .ring:
            "RingDevice"
        case .lockPc:
            "LockPc"
        case .deviceState:
            "DeviceState"
        case .pcStatus:
            "PcStatus"
        case nil:
            "empty"
        }
    }
}

/// The macOS test peripheral (ios/Tools/FenpoTestPeripheral) that plays the PC in device tests.
///
/// Its BLE channel key is fixed and public: it only exists so the Developer menu can pair with the
/// tool without the Windows app. Never use it for a real PC.
public enum TestPeripheral {
    /// PC id of the test peripheral.
    public static let pcID = "fenpo-test-peripheral"

    /// Name shown in the picker and in the app.
    public static let name = "Fenpo Test"

    /// Fixed BLE channel key shared with the tool.
    public static let bleChannelKey: [UInt8] = Array("fenpo-test-peripheral-ble-key-01".utf8)
}
