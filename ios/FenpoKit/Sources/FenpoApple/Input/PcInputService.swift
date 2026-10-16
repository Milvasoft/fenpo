#if os(iOS)
import FenpoCore
import FenpoProtocol
import Foundation

/// The phone as the PC's touchpad, keyboard and presentation remote (protocol/README.md,
/// "PC input"). Moves and scrolls are summed and sent once a frame while a remote screen is open;
/// everything goes out in order on the best open channel: Wi-Fi, otherwise Bluetooth (with fewer
/// frames, since BLE carries less).
@MainActor
public final class PcInputService {
    private let central: BLECentral
    private let wifi: WiFiChannel
    private var coalescer = PcInputCoalescer()
    private var ticker: Task<Void, Never>?
    private var users = 0
    private let outbox: AsyncStream<Fenpo_V1_PcInput>.Continuation
    private let log = FenpoLog("Input")

    /// Creates the service.
    /// - Parameters:
    ///   - central: Bluetooth channel.
    ///   - wifi: Wi-Fi channel.
    public init(central: BLECentral, wifi: WiFiChannel) {
        self.central = central
        self.wifi = wifi
        let (stream, outbox) = AsyncStream<Fenpo_V1_PcInput>.makeStream(bufferingPolicy: .bufferingNewest(256))
        self.outbox = outbox
        Task { [weak self] in
            for await input in stream {
                await self?.deliver(input)
            }
        }
    }

    /// Whether the PC on an open channel takes input.
    public var isAvailable: Bool {
        (wifi.state == .connected && wifi.session?.supports(.pcInput) == true)
            || (central.state == .ready && central.session?.supports(.pcInput) == true)
    }

    /// A remote screen opened: start sending frames and open Wi-Fi for the lower delay.
    public func begin() {
        users += 1
        guard ticker == nil else {
            return
        }
        Task {
            try? await wifi.connectIfNeeded()
        }
        ticker = Task { [weak self] in
            while !Task.isCancelled {
                let overWiFi = self?.wifi.state == .connected
                try? await Task.sleep(for: .milliseconds(overWiFi ? 16 : 33))
                self?.flush()
            }
        }
    }

    /// A remote screen closed.
    public func end() {
        users = max(0, users - 1)
        guard users == 0 else {
            return
        }
        flush()
        ticker?.cancel()
        ticker = nil
    }

    /// Adds to the pointer move of the next frame.
    /// - Parameters:
    ///   - dx: Horizontal move in the PC's pixels.
    ///   - dy: Vertical move.
    public func move(dx: Float, dy: Float) {
        coalescer.move(dx: dx, dy: dy)
    }

    /// Adds to the scroll of the next frame.
    /// - Parameters:
    ///   - dx: Horizontal notches.
    ///   - dy: Vertical notches, positive scrolls the content down.
    public func scroll(dx: Float, dy: Float) {
        coalescer.scroll(dx: dx, dy: dy)
    }

    /// Sends an input after the pending move and scroll.
    /// - Parameter input: Click, key, text or presenter input.
    public func send(_ input: Fenpo_V1_PcInput) {
        for item in coalescer.then(input) {
            outbox.yield(item)
        }
    }

    private func flush() {
        for item in coalescer.flush() {
            outbox.yield(item)
        }
    }

    private func deliver(_ input: Fenpo_V1_PcInput) async {
        if wifi.state == .connected {
            do {
                _ = try await wifi.send(.pcInput(input))
                return
            } catch {
                log.info("Input over Wi-Fi failed, trying Bluetooth: \(String(describing: error))")
            }
        }
        guard central.state == .ready else {
            return
        }
        do {
            try central.send(.pcInput(input))
        } catch {
            log.error("Input not sent: \(String(describing: error))")
        }
    }
}
#endif
