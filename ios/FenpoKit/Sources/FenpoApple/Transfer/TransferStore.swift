import FenpoCore
import Foundation

/// Transfer history in the App Group, shared by the app and the share extension. Newest first.
public struct TransferStore: Sendable {
    private static let key = "transfers.history"
    private static let limit = 200

    private let suiteName: String?

    /// Creates a store.
    /// - Parameter suiteName: UserDefaults suite; the App Group by default.
    public init(suiteName: String? = FenpoIdentifiers.appGroup) {
        self.suiteName = suiteName
    }

    private var defaults: UserDefaults {
        suiteName.flatMap(UserDefaults.init(suiteName:)) ?? .standard
    }

    /// All records, newest first.
    public func load() -> [TransferRecord] {
        guard
            let data = defaults.data(forKey: Self.key),
            let records = try? JSONDecoder().decode([TransferRecord].self, from: data)
        else {
            return []
        }
        return records
    }

    /// Adds records at the top, keeping the newest 200.
    /// - Parameter records: Finished transfers.
    public func add(_ records: [TransferRecord]) {
        guard !records.isEmpty else {
            return
        }
        let all = Array((records + load()).prefix(Self.limit))
        if let data = try? JSONEncoder().encode(all) {
            defaults.set(data, forKey: Self.key)
        }
    }

    /// Where received files go: the app's Documents folder, shown in the Files app under Fenpo.
    public static var inboxURL: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
    }
}
