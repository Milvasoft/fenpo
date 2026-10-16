import FenpoCore
import Foundation
import OSLog

/// Logs to the unified log and to a shared file the Developer menu shows and exports.
///
/// Never log clipboard contents, keys or tokens; log sizes and kinds instead.
public struct FenpoLog: Sendable {
    /// The shared file in the App Group container (Caches when the group is unavailable).
    public static let file: LogFile = {
        let container = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: FenpoIdentifiers.appGroup)
            ?? FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        return LogFile(directory: container.appendingPathComponent("Logs", isDirectory: true))
    }()

    private let logger: Logger
    private let category: String

    /// Creates a log for an area.
    /// - Parameter category: Area name, e.g. "BLE".
    public init(_ category: String) {
        self.category = category
        logger = Logger(subsystem: "com.fenpo", category: category)
    }

    /// Normal event.
    /// - Parameter message: Text.
    public func info(_ message: String) {
        logger.info("\(message, privacy: .public)")
        Self.file.append(.info, category: category, message)
    }

    /// Something failed.
    /// - Parameter message: Text.
    public func error(_ message: String) {
        logger.error("\(message, privacy: .public)")
        Self.file.append(.error, category: category, message)
    }

    /// Detail for debugging.
    /// - Parameter message: Text.
    public func debug(_ message: String) {
        logger.debug("\(message, privacy: .public)")
        Self.file.append(.debug, category: category, message)
    }
}
