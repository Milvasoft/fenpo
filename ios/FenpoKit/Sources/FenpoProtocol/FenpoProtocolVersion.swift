/// Version of the Fenpo protocol this build speaks.
///
/// Sent in `Envelope.version` and listed in `Hello.protocolVersions`.
public enum FenpoProtocolVersion {
    /// The protocol version used for every message this build sends.
    public static let current: UInt32 = 1
}
