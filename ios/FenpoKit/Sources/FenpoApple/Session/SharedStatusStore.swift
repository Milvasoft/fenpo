import FenpoCore
import Foundation
#if canImport(WidgetKit)
import WidgetKit
#endif

/// Keeps `SharedStatus` in the App Group so widgets can show it, and reloads them when it changes.
public struct SharedStatusStore: Sendable {
    private static let key = "status.shared"

    private let suiteName: String?

    /// Creates a store.
    /// - Parameter suiteName: UserDefaults suite; the App Group by default.
    public init(suiteName: String? = FenpoIdentifiers.appGroup) {
        self.suiteName = suiteName
    }

    private var defaults: UserDefaults {
        suiteName.flatMap(UserDefaults.init(suiteName:)) ?? .standard
    }

    /// The last written status.
    public func load() -> SharedStatus {
        guard
            let data = defaults.data(forKey: Self.key),
            let status = try? JSONDecoder().decode(SharedStatus.self, from: data)
        else {
            return .unpaired
        }
        return status
    }

    /// Writes the status and reloads the widgets when something visible changed.
    /// - Parameter status: New status.
    public func save(_ status: SharedStatus) {
        var previous = load()
        previous.updatedAt = status.updatedAt
        guard previous != status, let data = try? JSONEncoder().encode(status) else {
            return
        }
        defaults.set(data, forKey: Self.key)
        #if canImport(WidgetKit)
        WidgetCenter.shared.reloadAllTimelines()
        #endif
    }
}
