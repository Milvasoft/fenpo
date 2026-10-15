/// What the user gave to start pairing.
public enum PairingInput: Equatable, Sendable {
    /// Contents of the QR code on the PC (`fenpo://pair?...`).
    case qrCode(String)

    /// The 6 digit fallback code shown under the QR code.
    case manualCode(String)
}

/// Runs the pairing steps. The real implementation sits on the TLS and BLE channels; `SamplePairing` stands in until then.
public protocol PairingProviding: Sendable {
    /// Opens the TLS data channel and exchanges `PairingRequest` / `PairingResult`.
    /// - Parameter input: QR contents or manual code.
    /// - Returns: The PC that accepted the pairing.
    func startDataChannel(_ input: PairingInput) async throws -> PairedPC

    /// Bonds over Bluetooth through AccessorySetupKit.
    func bondBluetooth() async throws

    /// Waits until iOS has asked about notification sharing and the PC sees ANCS.
    func waitForNotificationSharing() async throws
}

/// Pairing that always succeeds after short pauses, for previews and until the channels exist.
public struct SamplePairing: PairingProviding {
    /// Creates a sample pairing.
    public init() {
    }

    public func startDataChannel(_ input: PairingInput) async throws -> PairedPC {
        try await Task.sleep(for: .milliseconds(800))
        return SampleSession.pairedSnapshot().pc ?? PairedPC(id: "sample-pc", name: "BUGRA-PC", systemName: "Windows 11")
    }

    public func bondBluetooth() async throws {
        try await Task.sleep(for: .milliseconds(1_200))
    }

    public func waitForNotificationSharing() async throws {
        try await Task.sleep(for: .milliseconds(1_500))
    }
}
