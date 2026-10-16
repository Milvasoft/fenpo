import ActivityKit
import Foundation

/// Live Activity of a large transfer (design: lock screen and Dynamic Island). Compiled into the
/// app, which starts and updates it, and the widget extension, which draws it.
struct TransferActivityAttributes: ActivityAttributes {
    /// What changes while the transfer runs.
    struct ContentState: Codable, Hashable {
        var bytesDone: UInt64
        var totalBytes: UInt64
        var filesLeft: Int

        /// Progress between 0 and 1.
        var fraction: Double {
            totalBytes == 0 ? 1 : min(1, Double(bytesDone) / Double(totalBytes))
        }
    }

    /// PC name.
    var pcName: String

    /// Number of files.
    var fileCount: Int

    /// The phone sends (true) or receives (false).
    var isSending: Bool
}
