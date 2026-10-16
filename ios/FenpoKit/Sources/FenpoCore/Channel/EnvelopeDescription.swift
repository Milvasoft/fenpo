import FenpoProtocol

extension Fenpo_V1_Envelope {
    /// Message type for logs, e.g. "ping". Never includes the contents.
    ///
    /// Read from the case name, so new protocol messages need no change here.
    public var bodyName: String {
        guard let body else {
            return "empty"
        }
        return Mirror(reflecting: body).children.first?.label ?? "unknown"
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
