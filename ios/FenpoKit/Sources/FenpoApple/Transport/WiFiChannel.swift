#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation

/// The long-lived Wi-Fi control connection (path ④) while Fenpo is in front, or for one job in an
/// extension. Carries what does not fit BLE: file transfer negotiation, camera control, large items.
@MainActor
public final class WiFiChannel {
    /// Connection state.
    public enum State: Equatable, Sendable {
        case idle
        case connecting
        case connected
    }

    /// Why waiting for a message ended.
    public enum Error: Swift.Error, Equatable, Sendable {
        /// The connection closed while waiting.
        case closed
    }

    /// Current state.
    public private(set) var state: State = .idle {
        didSet {
            if state != oldValue {
                log.info("Wi-Fi channel \(String(describing: self.state))")
                onStateChange?(state)
            }
        }
    }

    /// What the PC agreed to on this connection.
    public private(set) var session: NegotiatedSession?

    /// Address of the PC's control server on this connection; data and media connections go there too.
    public private(set) var endpoint: Endpoint?

    /// Called for every Envelope that is not a reply someone waits for and not a Ping.
    public var onEnvelope: ((Fenpo_V1_Envelope) -> Void)?

    /// Called when the state changes.
    public var onStateChange: ((State) -> Void)?

    private let client: DataChannelClient
    private var connection: TLSControlConnection?
    private var sequencer = EnvelopeSequencer()
    private var receiveTask: Task<Void, Never>?
    private var keepAliveTask: Task<Void, Never>?
    private var waiters: [UUID: (matches: (Fenpo_V1_Envelope) -> Bool, continuation: CheckedContinuation<Fenpo_V1_Envelope, Swift.Error>)] = [:]
    private let log = FenpoLog("WiFi")

    /// Creates a channel.
    /// - Parameter client: Opens connections with the stored pairing and identity.
    public init(client: DataChannelClient = DataChannelClient()) {
        self.client = client
    }

    /// The client this channel opens connections with.
    public var dataClient: DataChannelClient {
        client
    }

    /// Connects when not connected yet.
    public func connectIfNeeded() async throws {
        switch state {
        case .connected:
            return
        case .connecting:
            while state == .connecting {
                try await Task.sleep(for: .milliseconds(100))
            }
            if state != .connected {
                throw FenpoActionError.notConnected
            }
        case .idle:
            state = .connecting
            do {
                let channel = try await client.connect()
                connection = channel.connection
                session = channel.session
                sequencer = channel.sequencer
                endpoint = channel.connection.endpoint
                state = .connected
                startReceiving(channel.connection)
                startKeepAlive()
            } catch {
                state = .idle
                throw error
            }
        }
    }

    /// Closes the connection.
    public func disconnect() {
        receiveTask?.cancel()
        keepAliveTask?.cancel()
        if let connection {
            Task {
                await connection.cancel()
            }
        }
        closed()
    }

    /// Sends a message.
    /// - Parameters:
    ///   - body: Message to send.
    ///   - replyTo: Id of the request this answers, or 0.
    @discardableResult
    public func send(_ body: Fenpo_V1_Envelope.OneOf_Body, replyTo: UInt64 = 0) async throws -> UInt64 {
        guard let connection, state == .connected else {
            throw FenpoActionError.notConnected
        }
        let envelope = sequencer.envelope(body, replyTo: replyTo)
        try await connection.send(envelope)
        return envelope.id
    }

    /// Sends a request and waits for the Envelope that answers it (`reply_to` is its id). The wait
    /// is registered before the request leaves, so a fast answer is not missed.
    /// - Parameters:
    ///   - body: The request.
    ///   - timeout: Longest wait for the answer.
    public func request(_ body: Fenpo_V1_Envelope.OneOf_Body, timeout: Duration = .seconds(15)) async throws -> Fenpo_V1_Envelope {
        guard let connection, state == .connected else {
            throw FenpoActionError.notConnected
        }
        let envelope = sequencer.envelope(body)
        let requestID = envelope.id
        let waiterID = UUID()
        let timer = Task { [weak self] in
            try? await Task.sleep(for: timeout)
            self?.fail(waiter: waiterID, with: TimeoutError(duration: timeout))
        }
        defer {
            timer.cancel()
        }
        return try await withCheckedThrowingContinuation { continuation in
            waiters[waiterID] = ({ $0.replyTo == requestID }, continuation)
            Task {
                do {
                    try await connection.send(envelope)
                } catch {
                    self.fail(waiter: waiterID, with: error)
                }
            }
        }
    }

    /// Waits for the first incoming Envelope that matches. Register before sending the request.
    /// - Parameters:
    ///   - timeout: Longest wait.
    ///   - matches: Which Envelope ends the wait.
    public func next(timeout: Duration, where matches: @escaping (Fenpo_V1_Envelope) -> Bool) async throws -> Fenpo_V1_Envelope {
        let id = UUID()
        let timer = Task { [weak self] in
            try? await Task.sleep(for: timeout)
            self?.fail(waiter: id, with: TimeoutError(duration: timeout))
        }
        defer {
            timer.cancel()
        }
        return try await withCheckedThrowingContinuation { continuation in
            waiters[id] = (matches, continuation)
        }
    }

    /// Opens a data or media connection to the same PC.
    public func openStream() async throws -> TLSControlConnection {
        guard let endpoint else {
            throw FenpoActionError.notConnected
        }
        return try await client.openStream(to: endpoint)
    }

    // MARK: - Private

    private func startReceiving(_ connection: TLSControlConnection) {
        receiveTask = Task { [weak self] in
            while !Task.isCancelled {
                do {
                    let envelope = try await connection.receive()
                    self?.handle(envelope)
                } catch {
                    self?.log.info("Wi-Fi channel closed: \(String(describing: error))")
                    self?.closed()
                    return
                }
            }
        }
    }

    private func startKeepAlive() {
        keepAliveTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(30))
                var ping = Fenpo_V1_Ping()
                ping.nonce = UInt64.random(in: 1...UInt64.max)
                _ = try? await self?.send(.ping(ping))
            }
        }
    }

    private func handle(_ envelope: Fenpo_V1_Envelope) {
        if let waiter = waiters.first(where: { $0.value.matches(envelope) }) {
            waiters.removeValue(forKey: waiter.key)
            waiter.value.continuation.resume(returning: envelope)
            return
        }
        if case .ping(let ping) = envelope.body {
            var pong = Fenpo_V1_Pong()
            pong.nonce = ping.nonce
            Task {
                _ = try? await self.send(.pong(pong), replyTo: envelope.id)
            }
            return
        }
        if case .pong = envelope.body {
            return
        }
        log.info("Received \(envelope.bodyName) #\(envelope.id) over Wi-Fi")
        onEnvelope?(envelope)
    }

    private func fail(waiter id: UUID, with error: Swift.Error) {
        waiters.removeValue(forKey: id)?.continuation.resume(throwing: error)
    }

    private func closed() {
        keepAliveTask?.cancel()
        connection = nil
        session = nil
        endpoint = nil
        state = .idle
        let pending = waiters
        waiters.removeAll()
        for waiter in pending.values {
            waiter.continuation.resume(throwing: Error.closed)
        }
    }
}
#endif
