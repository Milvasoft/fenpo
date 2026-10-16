import CryptoKit
import FenpoCore
import FenpoProtocol
import Foundation
import Network
import OSLog
import Security
import Synchronization

/// Why the control connection failed.
public enum TLSConnectionError: Error, Equatable, Sendable {
    /// The PC closed the connection.
    case closed

    /// The connection was cancelled.
    case cancelled

    /// Network.framework reported an error.
    case network(String)
}

/// One TLS 1.3 control connection to the PC (path ④): pinned server certificate, the phone's
/// certificate as client identity, `[uint32 BE length][Envelope]` framing.
///
/// Receive from one task at a time.
public actor TLSControlConnection {
    private let connection: NWConnection
    private let queue = DispatchQueue(label: "com.fenpo.tls")
    private var decoder = EnvelopeFrameDecoder()
    private var pending: [[UInt8]] = []
    private let logger = Logger(subsystem: "com.fenpo.kit", category: "TLS")

    /// Endpoint this connection goes to.
    public nonisolated let endpoint: Endpoint

    /// Prepares a connection; nothing happens until `start()`.
    /// - Parameters:
    ///   - endpoint: PC address.
    ///   - pinnedServerSHA256: SHA-256 of the PC certificate (from the QR code or the pairing record).
    ///   - identity: The phone's identity, presented as client certificate.
    public init(endpoint: Endpoint, pinnedServerSHA256: [UInt8], identity: DeviceIdentity) {
        self.endpoint = endpoint
        let tls = NWProtocolTLS.Options()
        let options = tls.securityProtocolOptions
        sec_protocol_options_set_min_tls_protocol_version(options, .TLSv13)
        sec_protocol_options_set_max_tls_protocol_version(options, .TLSv13)
        if let localIdentity = sec_identity_create(identity.identity) {
            sec_protocol_options_set_local_identity(options, localIdentity)
        }
        let verifyQueue = DispatchQueue(label: "com.fenpo.tls.verify")
        sec_protocol_options_set_verify_block(options, { _, trust, complete in
            complete(Self.matches(trust: trust, pinned: pinnedServerSHA256))
        }, verifyQueue)

        let parameters = NWParameters(tls: tls, tcp: NWProtocolTCP.Options())
        connection = NWConnection(
            host: NWEndpoint.Host(endpoint.host),
            port: NWEndpoint.Port(rawValue: endpoint.port) ?? .any,
            using: parameters
        )
    }

    /// Connects and completes the TLS handshake.
    public func start() async throws {
        let once = Once<Void>()
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Swift.Error>) in
                once.set(continuation)
                connection.stateUpdateHandler = { state in
                    switch state {
                    case .ready:
                        once.resume(returning: ())
                    case .failed(let error), .waiting(let error):
                        once.resume(throwing: TLSConnectionError.network(String(describing: error)))
                    case .cancelled:
                        once.resume(throwing: TLSConnectionError.cancelled)
                    default:
                        break
                    }
                }
                connection.start(queue: queue)
            }
        } onCancel: {
            connection.cancel()
        }
        logger.info("TLS ready to \(self.endpoint.description, privacy: .public)")
    }

    /// Sends one Envelope.
    /// - Parameter envelope: Message to send.
    public func send(_ envelope: Fenpo_V1_Envelope) async throws {
        let frame = try EnvelopeFraming.frame(try envelope.serializedBytes())
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Swift.Error>) in
            connection.send(content: Data(frame), completion: .contentProcessed { error in
                if let error {
                    continuation.resume(throwing: TLSConnectionError.network(String(describing: error)))
                } else {
                    continuation.resume()
                }
            })
        }
    }

    /// Waits for the next Envelope.
    public func receive() async throws -> Fenpo_V1_Envelope {
        while pending.isEmpty {
            let bytes = try await receiveBytes()
            pending += try decoder.append(bytes)
        }
        return try Fenpo_V1_Envelope(serializedBytes: pending.removeFirst())
    }

    /// Closes the connection.
    public func cancel() {
        connection.cancel()
    }

    private func receiveBytes() async throws -> Data {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Data, Swift.Error>) in
            connection.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { data, _, isComplete, error in
                if let error {
                    continuation.resume(throwing: TLSConnectionError.network(String(describing: error)))
                } else if let data, !data.isEmpty {
                    continuation.resume(returning: data)
                } else if isComplete {
                    continuation.resume(throwing: TLSConnectionError.closed)
                } else {
                    continuation.resume(returning: Data())
                }
            }
        }
    }

    /// The leaf certificate's SHA-256 equals the pinned value. No CA trust is involved.
    private static func matches(trust: sec_trust_t, pinned: [UInt8]) -> Bool {
        let secTrust = sec_trust_copy_ref(trust).takeRetainedValue()
        guard
            let chain = SecTrustCopyCertificateChain(secTrust) as? [SecCertificate],
            let leaf = chain.first
        else {
            return false
        }
        return CertificateFingerprint.sha256(leaf) == pinned
    }
}

/// Resumes a continuation at most once; later calls are ignored.
final class Once<Value: Sendable>: Sendable {
    private let continuation = Mutex<CheckedContinuation<Value, Swift.Error>?>(nil)

    func set(_ continuation: CheckedContinuation<Value, Swift.Error>) {
        self.continuation.withLock { $0 = continuation }
    }

    func resume(returning value: Value) {
        take()?.resume(returning: value)
    }

    func resume(throwing error: Swift.Error) {
        take()?.resume(throwing: error)
    }

    private func take() -> CheckedContinuation<Value, Swift.Error>? {
        continuation.withLock { stored in
            defer { stored = nil }
            return stored
        }
    }
}
