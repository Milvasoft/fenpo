import FenpoProtocol

/// Gives every Envelope a sender-unique id, starting at 1 on each connection.
public struct EnvelopeSequencer: Sendable {
    private var nextID: UInt64 = 1

    /// Creates a sequencer for a new connection.
    public init() {
    }

    /// Wraps a message body in an Envelope.
    /// - Parameters:
    ///   - body: Message to send.
    ///   - replyTo: Id of the request this answers, or 0.
    public mutating func envelope(_ body: Fenpo_V1_Envelope.OneOf_Body, replyTo: UInt64 = 0) -> Fenpo_V1_Envelope {
        var envelope = Fenpo_V1_Envelope()
        envelope.version = FenpoProtocolVersion.current
        envelope.id = nextID
        envelope.replyTo = replyTo
        envelope.body = body
        nextID += 1
        return envelope
    }
}

/// What both sides agreed on after exchanging `Hello`.
public struct NegotiatedSession: Equatable, Sendable {
    /// Protocol version both sides speak.
    public let protocolVersion: UInt32

    /// Features both sides offer.
    public let capabilities: Set<Fenpo_V1_Capability>

    /// The other side's stable device id.
    public let peerDeviceID: String

    /// The other side's user visible name.
    public let peerName: String

    /// The other side's platform.
    public let peerPlatform: Fenpo_V1_DevicePlatform

    /// The feature is on for this connection.
    /// - Parameter capability: Feature to check.
    public func supports(_ capability: Fenpo_V1_Capability) -> Bool {
        capabilities.contains(capability)
    }
}

/// Building and checking `Hello` (docs/ARCHITECTURE.md, "El sıkışma").
public enum Handshake {
    /// Why the handshake failed.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The first message was not `Hello`.
        case expectedHello

        /// No protocol version in common.
        case noCommonVersion(remote: [UInt32])

        /// The peer sent an `Error` instead.
        case peerError(Fenpo_V1_ErrorCode, String)
    }

    /// Protocol versions this build speaks.
    public static let supportedVersions: [UInt32] = [FenpoProtocolVersion.current]

    /// Capabilities the iPhone app offers today.
    public static let phoneCapabilities: [Fenpo_V1_Capability] = [
        .clipboard,
        .fileTransfer,
        .openURL,
        .ring,
        .lockPc,
        .deviceState,
        .camera,
        .pcInput,
        .pcNotifications,
        .photos,
        .hotspot,
        .fileProvider,
        .screenMirror,
        .unlock,
    ]

    /// Builds this device's `Hello`.
    /// - Parameters:
    ///   - deviceID: Stable id created at first launch.
    ///   - deviceName: User visible name.
    ///   - appVersion: Semantic version of the app.
    ///   - osVersion: e.g. "iOS 27.0.1".
    ///   - model: Hardware model identifier, e.g. "iPhone14,5".
    ///   - capabilities: Offered features.
    public static func makeHello(
        deviceID: String,
        deviceName: String,
        appVersion: String,
        osVersion: String = "",
        model: String = "",
        capabilities: [Fenpo_V1_Capability] = phoneCapabilities
    ) -> Fenpo_V1_Hello {
        var hello = Fenpo_V1_Hello()
        hello.deviceID = deviceID
        hello.deviceName = deviceName
        hello.platform = .ios
        hello.appVersion = appVersion
        hello.protocolVersions = supportedVersions
        hello.capabilities = capabilities
        hello.osVersion = osVersion
        hello.model = model
        return hello
    }

    /// Checks the peer's first message and agrees on version and features.
    /// - Parameters:
    ///   - local: The `Hello` this side sent.
    ///   - remote: The first Envelope received from the peer.
    public static func negotiate(local: Fenpo_V1_Hello, remote: Fenpo_V1_Envelope) throws(Error) -> NegotiatedSession {
        switch remote.body {
        case .hello(let hello):
            return try negotiate(local: local, remote: hello)
        case .error(let error):
            throw .peerError(error.code, error.message)
        default:
            throw .expectedHello
        }
    }

    /// Agrees on version and features.
    /// - Parameters:
    ///   - local: The `Hello` this side sent.
    ///   - remote: The peer's `Hello`.
    public static func negotiate(local: Fenpo_V1_Hello, remote: Fenpo_V1_Hello) throws(Error) -> NegotiatedSession {
        guard let version = Set(local.protocolVersions).intersection(remote.protocolVersions).max() else {
            throw .noCommonVersion(remote: remote.protocolVersions)
        }
        let known = Set(Fenpo_V1_Capability.allCases).subtracting([.unspecified])
        let capabilities = Set(local.capabilities)
            .intersection(remote.capabilities)
            .intersection(known)
        return NegotiatedSession(
            protocolVersion: version,
            capabilities: capabilities,
            peerDeviceID: remote.deviceID,
            peerName: remote.deviceName,
            peerPlatform: remote.platform
        )
    }

    /// The automatic answer to a control message, if it needs one (`Ping` → `Pong`).
    /// - Parameters:
    ///   - envelope: Received Envelope.
    ///   - sequencer: Id source of the connection.
    public static func automaticReply(to envelope: Fenpo_V1_Envelope, sequencer: inout EnvelopeSequencer) -> Fenpo_V1_Envelope? {
        switch envelope.body {
        case .ping(let ping):
            var pong = Fenpo_V1_Pong()
            pong.nonce = ping.nonce
            return sequencer.envelope(.pong(pong), replyTo: envelope.id)
        default:
            return nil
        }
    }
}
