import ActivityKit
import FenpoApple
import FenpoCore
import UIKit

/// Shows large transfers as a Live Activity and keeps the app running in the background while any
/// transfer moves, so a transfer started in Fenpo survives a switch to another app.
@MainActor
final class TransferActivities {
    /// Smaller transfers end before a Live Activity would be worth it.
    private static let minimumBytes: UInt64 = 10 * 1024 * 1024

    /// Live Activity id by transfer id.
    private var activityIDs: [String: String] = [:]
    private var lastUpdate: [String: Date] = [:]
    private var backgroundTask: UIBackgroundTaskIdentifier = .invalid
    private let log = FenpoLog("LiveActivity")

    /// Creates the controller and ends activities a previous run left behind.
    init() {
        Self.end { _ in true }
    }

    /// Follows the transfers in progress.
    /// - Parameters:
    ///   - transfers: Transfers from the latest snapshot.
    ///   - pcName: Paired PC's name.
    func update(_ transfers: [ActiveTransfer], pcName: String) {
        keepRunning(!transfers.isEmpty)

        let current = Set(transfers.map(\.id))
        let finished = activityIDs.filter { !current.contains($0.key) }
        for (transferID, _) in finished {
            activityIDs[transferID] = nil
            lastUpdate[transferID] = nil
        }
        if !finished.isEmpty {
            let ids = Set(finished.values)
            Self.end { ids.contains($0) }
        }

        guard ActivityAuthorizationInfo().areActivitiesEnabled else {
            return
        }
        for transfer in transfers where transfer.totalBytes >= Self.minimumBytes {
            let state = TransferActivityAttributes.ContentState(bytesDone: transfer.bytesDone, totalBytes: transfer.totalBytes, filesLeft: transfer.filesLeft)
            if let activityID = activityIDs[transfer.id] {
                // The system limits how often an activity may change; once a second is plenty.
                if let last = lastUpdate[transfer.id], Date().timeIntervalSince(last) < 1 {
                    continue
                }
                lastUpdate[transfer.id] = Date()
                Self.update(activityID, to: state)
            } else {
                let attributes = TransferActivityAttributes(pcName: pcName, fileCount: transfer.fileCount, isSending: transfer.direction == .toPC)
                do {
                    let activity = try Activity<TransferActivityAttributes>.request(attributes: attributes, content: ActivityContent(state: state, staleDate: nil))
                    activityIDs[transfer.id] = activity.id
                    lastUpdate[transfer.id] = Date()
                    log.info("Live Activity started")
                } catch {
                    log.error("Live Activity failed: \(String(describing: error))")
                }
            }
        }
    }

    private func keepRunning(_ needed: Bool) {
        if needed, backgroundTask == .invalid {
            backgroundTask = UIApplication.shared.beginBackgroundTask(withName: "Fenpo transfer") {
                MainActor.assumeIsolated {
                    self.keepRunning(false)
                }
            }
        } else if !needed, backgroundTask != .invalid {
            UIApplication.shared.endBackgroundTask(backgroundTask)
            backgroundTask = .invalid
        }
    }

    private nonisolated static func update(_ activityID: String, to state: TransferActivityAttributes.ContentState) {
        Task.detached {
            for activity in Activity<TransferActivityAttributes>.activities where activity.id == activityID {
                await activity.update(ActivityContent(state: state, staleDate: nil))
            }
        }
    }

    private nonisolated static func end(where matches: @escaping @Sendable (String) -> Bool) {
        Task.detached {
            for activity in Activity<TransferActivityAttributes>.activities where matches(activity.id) {
                await activity.end(nil, dismissalPolicy: .immediate)
            }
        }
    }
}
