#if os(iOS)
import FenpoCore
import Foundation
import Network

/// Browses `_fenpo._tcp` with Bonjour while the pairing scanner is open. It makes iOS ask for local
/// network access before the first TLS connection, and logs what it finds so the DNS-SD record of
/// the PC can be checked on a device.
@MainActor
public final class PCBrowser {
    private var browser: NWBrowser?
    private let log = FenpoLog("Bonjour")

    /// Creates the browser.
    public init() {
    }

    /// Starts browsing; safe to call again.
    public func start() {
        guard browser == nil else {
            return
        }
        let browser = NWBrowser(for: .bonjourWithTXTRecord(type: FenpoIdentifiers.bonjourServiceType, domain: nil), using: .tcp)
        let log = log
        browser.stateUpdateHandler = { state in
            log.info("Browser \(String(describing: state))")
        }
        browser.browseResultsChangedHandler = { results, _ in
            for result in results {
                var text = ""
                if case .bonjour(let record) = result.metadata {
                    text = record.dictionary.map { "\($0.key)=\($0.value)" }.sorted().joined(separator: " ")
                }
                log.info("Found \(String(describing: result.endpoint)) \(text)")
            }
        }
        browser.start(queue: .main)
        self.browser = browser
    }

    /// Stops browsing.
    public func stop() {
        browser?.cancel()
        browser = nil
    }
}
#endif
