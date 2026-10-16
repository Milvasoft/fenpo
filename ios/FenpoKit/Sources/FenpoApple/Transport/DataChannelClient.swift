#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation
import UIKit

extension LocalDevice {
    /// This phone's `Hello`.
    @MainActor
    public static func makeHello() -> Fenpo_V1_Hello {
        Handshake.makeHello(
            deviceID: deviceID,
            deviceName: UIDevice.current.name,
            appVersion: appVersion,
            osVersion: osVersion,
            model: model
        )
    }
}

/// An open control connection after `Hello`.
public struct DataChannel: Sendable {
    /// The TLS connection; cancel it when done.
    public let connection: TLSControlConnection

    /// What the PC agreed to.
    public let session: NegotiatedSession

    /// Id source of this connection, already past the `Hello`.
    public var sequencer: EnvelopeSequencer
}

/// Opens the Wi-Fi control connection (path ④) to the paired PC from any process, including the
/// share extension: pairing record from the App Group, identity from the shared keychain.
public struct DataChannelClient: Sendable {
    private let store: PairedPCStore
    private let identityStore: DeviceIdentityStore

    /// Creates the client.
    public init(store: PairedPCStore = PairedPCStore(), identityStore: DeviceIdentityStore = DeviceIdentityStore()) {
        self.store = store
        self.identityStore = identityStore
    }

    /// Connects to the first reachable address and exchanges `Hello`.
    @MainActor
    public func connect() async throws -> DataChannel {
        guard let record = store.load() else {
            throw FenpoActionError.notPaired
        }
        let endpoints = record.endpoints.compactMap(Endpoint.init(parsing:))
        guard !endpoints.isEmpty, !record.certificateSHA256.isEmpty else {
            throw FenpoActionError.notConnected
        }
        let identity = try identityStore.loadOrCreate()
        let hello = LocalDevice.makeHello()
        let log = FenpoLog("DataChannel")

        for endpoint in endpoints {
            let connection = TLSControlConnection(endpoint: endpoint, pinnedServerSHA256: record.certificateSHA256, identity: identity)
            do {
                try await withTimeout(.seconds(5)) {
                    try await connection.start()
                }
                var sequencer = EnvelopeSequencer()
                try await connection.send(sequencer.envelope(.hello(hello)))
                let answer = try await withTimeout(.seconds(5)) {
                    try await connection.receive()
                }
                let session = try Handshake.negotiate(local: hello, remote: answer)
                return DataChannel(connection: connection, session: session, sequencer: sequencer)
            } catch {
                log.info("No data channel at \(endpoint.description): \(String(describing: error))")
                await connection.cancel()
            }
        }
        throw FenpoActionError.notConnected
    }

    /// Opens a data or media connection to the same PC: same pinned TLS, no Hello. The caller writes
    /// the first frame (`TransferStreamHeader` or `MediaStreamOpen`).
    /// - Parameter endpoint: Address of the PC's control server.
    @MainActor
    public func openStream(to endpoint: Endpoint) async throws -> TLSControlConnection {
        guard let record = store.load() else {
            throw FenpoActionError.notPaired
        }
        let identity = try identityStore.loadOrCreate()
        let connection = TLSControlConnection(endpoint: endpoint, pinnedServerSHA256: record.certificateSHA256, identity: identity)
        do {
            try await withTimeout(.seconds(10)) {
                try await connection.start()
            }
        } catch {
            await connection.cancel()
            throw FenpoActionError.notConnected
        }
        return connection
    }

    /// Sends `OpenUrl` over Wi-Fi ("Open on PC" from the share extension).
    /// - Parameter url: Link to open on the PC.
    @MainActor
    public func openOnPC(_ url: URL) async throws {
        var channel = try await connect()
        let connection = channel.connection
        defer {
            Task {
                await connection.cancel()
            }
        }
        guard channel.session.supports(.openURL) else {
            throw FenpoActionError.notConnected
        }
        var message = Fenpo_V1_OpenUrl()
        message.url = url.absoluteString
        try await connection.send(channel.sequencer.envelope(.openURL(message)))
    }
}
#endif
