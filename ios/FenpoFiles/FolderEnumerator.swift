import FenpoApple
import FenpoProtocol
import FileProvider
import Foundation

/// Lists one folder of the PC. Changes are found by listing again and comparing with the last listing,
/// which is kept on disk so deletions show up after the extension was restarted too.
final class FolderEnumerator: NSObject, NSFileProviderEnumerator {
    private let path: String

    /// Creates an enumerator.
    /// - Parameter path: Folder path on the PC, "" for the shared folders.
    init(path: String) {
        self.path = path
    }

    func invalidate() {
    }

    func enumerateItems(for observer: any NSFileProviderEnumerationObserver, startingAt page: NSFileProviderPage) {
        let observer = Unchecked(observer)
        let path = path
        Task { @MainActor in
            do {
                let items = try await FilesSession.shared.items(in: path)
                ListingCache.store(items.map(\.itemIdentifier.rawValue), for: path)
                observer.value.didEnumerate(items)
                observer.value.finishEnumerating(upTo: nil)
            } catch {
                observer.value.finishEnumeratingWithError(FilesSession.providerError(error))
            }
        }
    }

    func enumerateChanges(for observer: any NSFileProviderChangeObserver, from anchor: NSFileProviderSyncAnchor) {
        let observer = Unchecked(observer)
        let path = path
        Task { @MainActor in
            do {
                let items = try await FilesSession.shared.items(in: path)
                let now = Set(items.map(\.itemIdentifier.rawValue))
                let gone = ListingCache.load(for: path).filter { !now.contains($0) }
                ListingCache.store(Array(now), for: path)
                if !gone.isEmpty {
                    observer.value.didDeleteItems(withIdentifiers: gone.map(NSFileProviderItemIdentifier.init))
                }
                observer.value.didUpdate(items)
                observer.value.finishEnumeratingChanges(upTo: ListingCache.anchor(), moreComing: false)
            } catch {
                observer.value.finishEnumeratingWithError(FilesSession.providerError(error))
            }
        }
    }

    func currentSyncAnchor(completionHandler: @escaping (NSFileProviderSyncAnchor?) -> Void) {
        completionHandler(ListingCache.anchor())
    }
}

/// The working set: Fenpo keeps nothing offline, so it is empty.
final class EmptyEnumerator: NSObject, NSFileProviderEnumerator {
    func invalidate() {
    }

    func enumerateItems(for observer: any NSFileProviderEnumerationObserver, startingAt page: NSFileProviderPage) {
        observer.finishEnumerating(upTo: nil)
    }

    func enumerateChanges(for observer: any NSFileProviderChangeObserver, from anchor: NSFileProviderSyncAnchor) {
        observer.finishEnumeratingChanges(upTo: anchor, moreComing: false)
    }

    func currentSyncAnchor(completionHandler: @escaping (NSFileProviderSyncAnchor?) -> Void) {
        completionHandler(NSFileProviderSyncAnchor(Data("empty".utf8)))
    }
}

/// The identifiers of each folder's last listing, in the extension's caches.
enum ListingCache {
    private static var url: URL {
        FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("pc-listings.json")
    }

    static func load(for path: String) -> [String] {
        all()[path] ?? []
    }

    static func store(_ identifiers: [String], for path: String) {
        var listings = all()
        listings[path] = identifiers
        if let data = try? JSONEncoder().encode(listings) {
            try? data.write(to: url, options: .atomic)
        }
    }

    /// Anchors only need to change; the listing itself is compared.
    static func anchor() -> NSFileProviderSyncAnchor {
        NSFileProviderSyncAnchor(Data("\(Date().timeIntervalSince1970)".utf8))
    }

    private static func all() -> [String: [String]] {
        guard let data = try? Data(contentsOf: url) else {
            return [:]
        }
        return (try? JSONDecoder().decode([String: [String]].self, from: data)) ?? [:]
    }
}

/// Carries a value the File Provider API hands over without `Sendable` into a main-actor task.
struct Unchecked<Value>: @unchecked Sendable {
    let value: Value

    init(_ value: Value) {
        self.value = value
    }
}
