@testable import FenpoApple
import FenpoCore
import FenpoProtocol
import Foundation
import Network
import Security
import Synchronization
import Testing

/// TLS 1.3 between `TLSControlConnection` and an in-process server that plays the PC:
/// pinned server certificate, the phone's certificate as client identity, framed Ping/Pong.
@Suite(.serialized)
struct TLSLoopbackTests {
    let keychain = Keychain(accessGroup: nil)

    @Test
    func pinnedHandshakeCarriesTheClientCertificate() async throws {
        let serverStore = DeviceIdentityStore(keychain: keychain, label: "test.tls.server")
        let clientStore = DeviceIdentityStore(keychain: keychain, label: "test.tls.client")
        defer {
            try? serverStore.delete()
            try? clientStore.delete()
        }
        let pc = try serverStore.loadOrCreate(commonName: "Test PC")
        let phone = try clientStore.loadOrCreate(commonName: "Test iPhone")

        let server = try await EchoServer.start(identity: pc)
        defer {
            server.stop()
        }

        let connection = TLSControlConnection(
            endpoint: Endpoint(host: "127.0.0.1", port: server.port),
            pinnedServerSHA256: pc.fingerprint,
            identity: phone
        )
        try await withTimeout(.seconds(10)) {
            try await connection.start()
        }

        var sequencer = EnvelopeSequencer()
        var ping = Fenpo_V1_Ping()
        ping.nonce = 7
        try await connection.send(sequencer.envelope(.ping(ping)))
        let reply = try await withTimeout(.seconds(10)) {
            try await connection.receive()
        }
        await connection.cancel()

        #expect(reply.pong.nonce == 7)
        #expect(reply.replyTo == 1)
        #expect(server.clientFingerprint == phone.fingerprint)
    }

    @Test
    func wrongPinStopsTheHandshake() async throws {
        let serverStore = DeviceIdentityStore(keychain: keychain, label: "test.tls.server2")
        let clientStore = DeviceIdentityStore(keychain: keychain, label: "test.tls.client2")
        defer {
            try? serverStore.delete()
            try? clientStore.delete()
        }
        let pc = try serverStore.loadOrCreate(commonName: "Test PC")
        let phone = try clientStore.loadOrCreate(commonName: "Test iPhone")

        let server = try await EchoServer.start(identity: pc)
        defer {
            server.stop()
        }

        var wrongPin = pc.fingerprint
        wrongPin[0] ^= 0xFF
        let connection = TLSControlConnection(
            endpoint: Endpoint(host: "127.0.0.1", port: server.port),
            pinnedServerSHA256: wrongPin,
            identity: phone
        )
        await #expect(throws: (any Error).self) {
            try await withTimeout(.seconds(10)) {
                try await connection.start()
            }
        }
        await connection.cancel()
    }
}

/// A TLS 1.3 server on 127.0.0.1 that requires a client certificate and answers one Ping.
final class EchoServer: @unchecked Sendable {
    private let listener: NWListener
    private let queue: DispatchQueue
    private let seenClient: FingerprintBox
    private(set) var port: UInt16 = 0

    /// Fingerprint of the certificate the client presented.
    var clientFingerprint: [UInt8]? {
        seenClient.value.withLock { $0 }
    }

    static func start(identity: DeviceIdentity) async throws -> EchoServer {
        let server = try EchoServer(identity: identity)
        server.port = try await server.listen()
        return server
    }

    private init(identity: DeviceIdentity) throws {
        let queue = DispatchQueue(label: "test.echo")
        let seen = FingerprintBox()
        let tls = NWProtocolTLS.Options()
        let options = tls.securityProtocolOptions
        sec_protocol_options_set_min_tls_protocol_version(options, .TLSv13)
        guard let localIdentity = sec_identity_create(identity.identity) else {
            throw DeviceIdentityError.certificateRejected
        }
        sec_protocol_options_set_local_identity(options, localIdentity)
        sec_protocol_options_set_peer_authentication_required(options, true)
        sec_protocol_options_set_verify_block(options, { _, trust, complete in
            let secTrust = sec_trust_copy_ref(trust).takeRetainedValue()
            if let leaf = (SecTrustCopyCertificateChain(secTrust) as? [SecCertificate])?.first {
                let fingerprint = CertificateFingerprint.sha256(leaf)
                seen.value.withLock { $0 = fingerprint }
            }
            complete(true)
        }, queue)

        let parameters = NWParameters(tls: tls, tcp: NWProtocolTCP.Options())
        parameters.requiredLocalEndpoint = NWEndpoint.hostPort(host: "127.0.0.1", port: .any)
        self.queue = queue
        self.seenClient = seen
        listener = try NWListener(using: parameters)
    }

    func stop() {
        listener.cancel()
    }

    private func listen() async throws -> UInt16 {
        let once = Once<UInt16>()
        let listener = listener
        let queue = queue
        return try await withCheckedThrowingContinuation { continuation in
            once.set(continuation)
            listener.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    once.resume(returning: listener.port?.rawValue ?? 0)
                case .failed(let error):
                    once.resume(throwing: error)
                default:
                    break
                }
            }
            listener.newConnectionHandler = { connection in
                connection.start(queue: queue)
                connection.receive(minimumIncompleteLength: 1, maximumLength: 65_536) { data, _, _, _ in
                    var decoder = EnvelopeFrameDecoder()
                    var sequencer = EnvelopeSequencer()
                    guard
                        let data,
                        let frame = try? decoder.append(data).first,
                        let request = try? Fenpo_V1_Envelope(serializedBytes: frame),
                        let reply = Handshake.automaticReply(to: request, sequencer: &sequencer),
                        let bytes = try? EnvelopeFraming.frame(reply.serializedBytes())
                    else {
                        return
                    }
                    connection.send(content: Data(bytes), completion: .contentProcessed { _ in })
                }
            }
            listener.start(queue: queue)
        }
    }
}

/// Shared slot for the fingerprint the server's verify block sees.
final class FingerprintBox: Sendable {
    let value = Mutex<[UInt8]?>(nil)
}
