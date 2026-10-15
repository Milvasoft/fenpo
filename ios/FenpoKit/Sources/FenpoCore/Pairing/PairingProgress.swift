/// Progress of the steps after the QR code is scanned: data channel, Bluetooth bond, notification sharing.
public struct PairingProgress: Equatable, Sendable {
    /// A pairing step, in the order they run.
    public enum Step: CaseIterable, Sendable {
        /// TLS data channel over Wi-Fi.
        case dataChannel

        /// Bluetooth bond through AccessorySetupKit.
        case bluetooth

        /// iOS asks whether to share notifications with the PC.
        case notifications
    }

    /// State of a single step.
    public enum State: Equatable, Sendable {
        case queued
        case inProgress
        case done
        case failed
    }

    private var states: [Step: State]

    /// Starts with the data channel in progress and the rest queued.
    public init() {
        states = [.dataChannel: .inProgress, .bluetooth: .queued, .notifications: .queued]
    }

    /// State of the given step.
    /// - Parameter step: Step to look up.
    public func state(of step: Step) -> State {
        states[step] ?? .queued
    }

    /// The first step that is not done, or `nil` when everything is done.
    public var current: Step? {
        Step.allCases.first { state(of: $0) != .done }
    }

    /// Every step is done.
    public var isFinished: Bool {
        current == nil
    }

    /// Marks a step done and starts the next queued one.
    /// - Parameter step: Step that finished.
    public mutating func complete(_ step: Step) {
        states[step] = .done
        if let next = current, state(of: next) == .queued {
            states[next] = .inProgress
        }
    }

    /// Marks a step failed.
    /// - Parameter step: Step that failed.
    public mutating func fail(_ step: Step) {
        states[step] = .failed
    }

    /// Restarts a failed step.
    /// - Parameter step: Step to retry.
    public mutating func retry(_ step: Step) {
        if state(of: step) == .failed {
            states[step] = .inProgress
        }
    }
}
