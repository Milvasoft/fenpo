/// The operation did not finish in time.
public struct TimeoutError: Error, Equatable, Sendable {
    /// The limit that passed.
    public let duration: Duration

    /// Creates a timeout error.
    /// - Parameter duration: The limit that passed.
    public init(duration: Duration) {
        self.duration = duration
    }
}

/// Runs an operation and cancels it when it takes longer than `duration`.
/// - Parameters:
///   - duration: Time limit.
///   - operation: Work to run.
public func withTimeout<T: Sendable>(
    _ duration: Duration,
    operation: @escaping @Sendable () async throws -> T
) async throws -> T {
    try await withThrowingTaskGroup(of: T.self) { group in
        group.addTask {
            try await operation()
        }
        group.addTask {
            try await Task.sleep(for: duration)
            throw TimeoutError(duration: duration)
        }
        defer {
            group.cancelAll()
        }
        guard let result = try await group.next() else {
            throw TimeoutError(duration: duration)
        }
        return result
    }
}
