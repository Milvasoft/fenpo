import FenpoApple
import FenpoCore
import Foundation
import Observation
import UniformTypeIdentifiers

/// What was shared and what the extension is doing with it.
@MainActor
@Observable
final class ShareModel {
    /// Progress of "Open on PC".
    enum State: Equatable {
        case idle
        case sending
        case done
        case failed(FenpoActionError)
    }

    /// Paired PC name, or `nil` before pairing.
    let pcName: String?

    /// The shared link, once loaded.
    private(set) var url: URL?

    /// Files or photos were shared (sending them waits for the Wi-Fi transfer).
    private(set) var hasFiles = false

    private(set) var state: State = .idle

    private let items: [NSExtensionItem]
    private let client = DataChannelClient()
    private let log = FenpoLog("Share")

    /// Creates the model.
    /// - Parameter items: Input items of the extension context.
    init(items: [NSExtensionItem], pcName: String? = PairedPCStore().load()?.name) {
        self.items = items
        self.pcName = pcName
    }

    /// Finds a link among the shared items.
    func load() async {
        for provider in items.flatMap({ $0.attachments ?? [] }) {
            if provider.hasItemConformingToTypeIdentifier(UTType.url.identifier) {
                if let url = try? await provider.loadItem(forTypeIdentifier: UTType.url.identifier) as? URL, !url.isFileURL {
                    self.url = url
                    return
                }
            }
            if provider.hasItemConformingToTypeIdentifier(UTType.data.identifier) {
                hasFiles = true
            }
        }
    }

    /// Opens the link on the PC over Wi-Fi.
    func openOnPC() async {
        guard let url else {
            return
        }
        state = .sending
        do {
            try await client.openOnPC(url)
            state = .done
            log.info("Open on PC sent from the share extension")
        } catch let error as FenpoActionError {
            state = .failed(error)
        } catch {
            log.error("Open on PC failed: \(String(describing: error))")
            state = .failed(.notConnected)
        }
    }
}
