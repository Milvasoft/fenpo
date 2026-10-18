import FenpoApple
import FenpoCore
import Foundation
import Observation
import UIKit
import UniformTypeIdentifiers

/// What was shared and what the extension is doing with it. The extension talks to the PC over
/// its own Wi-Fi connection; the BLE channel belongs to the app.
@MainActor
@Observable
final class ShareModel {
    /// What the last button started, for the message when it is done.
    enum Job: Equatable {
        case openOnPC
        case sendFiles
        case copyToPC
    }

    /// Progress of the current job.
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

    /// Shared files copied into the extension's temporary folder.
    private(set) var files: [URL] = []

    /// Shared plain text (not a link or a file), once loaded.
    private(set) var text: String?

    /// The job the last button started.
    private(set) var job: Job?

    /// Items are still loading.
    private(set) var isLoading = true

    private(set) var state: State = .idle

    /// Bytes sent so far and in total, while sending files.
    private(set) var progress: (done: UInt64, total: UInt64)?

    private let items: [NSExtensionItem]
    private let client = DataChannelClient()
    private var wifi: WiFiChannel?
    private var transfers: FileTransferService?
    private let log = FenpoLog("Share")

    /// Creates the model.
    /// - Parameter items: Input items of the extension context.
    init(items: [NSExtensionItem], pcName: String? = PairedPCStore().load()?.name) {
        self.items = items
        self.pcName = pcName
    }

    /// Finds a link or files among the shared items.
    func load() async {
        defer {
            isLoading = false
        }
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("Share", isDirectory: true)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)

        for provider in items.flatMap({ $0.attachments ?? [] }) {
            if provider.hasItemConformingToTypeIdentifier(UTType.url.identifier),
               !provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier),
               let url = try? await provider.loadItem(forTypeIdentifier: UTType.url.identifier) as? URL,
               !url.isFileURL {
                self.url = url
                continue
            }
            if text == nil,
               provider.hasItemConformingToTypeIdentifier(UTType.plainText.identifier),
               !provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier),
               let item = try? await provider.loadItem(forTypeIdentifier: UTType.plainText.identifier) {
                if let string = item as? String {
                    text = string
                } else if let data = item as? Data {
                    text = String(data: data, encoding: .utf8)
                }
                if text != nil {
                    continue
                }
            }
            guard let type = provider.registeredContentTypes.first(where: { $0.conforms(to: .data) || $0.conforms(to: .fileURL) }) else {
                continue
            }
            do {
                let existing = Set(files.map(\.lastPathComponent))
                files.append(try await copy(provider, type: type, into: folder, existing: existing))
            } catch {
                log.error("Could not load a shared item: \(String(describing: error))")
            }
        }
        // A shared page often brings its title as text too; the link is what counts.
        if url != nil {
            text = nil
        }
    }

    /// The single shared image, which can go to the PC's clipboard.
    var sharedImage: URL? {
        guard files.count == 1, let file = files.first, UTType(filenameExtension: file.pathExtension)?.conforms(to: .image) == true else {
            return nil
        }
        return file
    }

    /// Something that fits the PC's clipboard: text, a link or one image.
    var canCopyToPC: Bool {
        text?.isEmpty == false || url != nil || sharedImage != nil
    }

    /// Puts the shared text, link or image on the PC's clipboard over Wi-Fi.
    func copyToPC() async {
        let payload: ClipboardPayload
        if let text, !text.isEmpty {
            payload = ClipboardPayload(.text(text))
        } else if let url {
            payload = ClipboardPayload(.url(url))
        } else if let file = sharedImage,
                  let data = try? Data(contentsOf: file),
                  let image = UIImage(data: data),
                  let encoded = ClipboardImageEncoder.encode(image, original: data, type: UTType(filenameExtension: file.pathExtension)) {
            payload = ClipboardPayload(.image(encoded.data, mimeType: encoded.mimeType))
        } else {
            state = .failed(.emptyText)
            return
        }
        job = .copyToPC
        log.info("Share extension copies to the PC clipboard: \(payload.logDescription)")
        await run {
            try await self.client.sendClipboard(payload)
        }
    }

    /// Copies one shared item; the system deletes its file as soon as the handler returns.
    private func copy(_ provider: NSItemProvider, type: UTType, into folder: URL, existing: Set<String>) async throws -> URL {
        let suggested = provider.suggestedName
        return try await withCheckedThrowingContinuation { continuation in
            _ = provider.loadFileRepresentation(for: type, openInPlace: false) { source, _, error in
                guard let source else {
                    continuation.resume(throwing: error ?? CocoaError(.fileNoSuchFile))
                    return
                }
                let proposed = suggested.map { name in
                    source.pathExtension.isEmpty || name.hasSuffix(".\(source.pathExtension)") ? name : "\(name).\(source.pathExtension)"
                } ?? source.lastPathComponent
                let target = folder.appendingPathComponent(TransferPlan.safeFileName(proposed, existing: existing))
                do {
                    try? FileManager.default.removeItem(at: target)
                    try FileManager.default.copyItem(at: source, to: target)
                    continuation.resume(returning: target)
                } catch {
                    continuation.resume(throwing: error)
                }
            }
        }
    }

    /// Opens the link on the PC over Wi-Fi.
    func openOnPC() async {
        guard let url else {
            return
        }
        job = .openOnPC
        await run {
            try await self.client.openOnPC(url)
        }
    }

    /// Sends the shared files to the PC over Wi-Fi.
    func sendFiles() async {
        guard !files.isEmpty else {
            return
        }
        job = .sendFiles
        let wifi = WiFiChannel(client: client)
        let transfers = FileTransferService(wifi: wifi)
        transfers.onChange = { [weak self, weak transfers] in
            guard let self, let active = transfers?.active.values.first else {
                return
            }
            self.progress = (active.bytesDone, active.totalBytes)
        }
        self.wifi = wifi
        self.transfers = transfers
        let outgoing = files
        await run {
            let files = try outgoing.map(OutgoingFile.init(sharedFileAt:))
            try await transfers.send(files)
        }
        wifi.disconnect()
    }

    private func run(_ job: @MainActor () async throws -> Void) async {
        state = .sending
        do {
            try await job()
            state = .done
            log.info("Share extension job finished")
        } catch let error as FenpoActionError {
            state = .failed(error)
        } catch {
            log.error("Share extension job failed: \(String(describing: error))")
            state = .failed(.notConnected)
        }
    }
}

extension OutgoingFile {
    /// Describes a shared file for an offer.
    /// - Parameter url: Copy in the extension's temporary folder.
    init(sharedFileAt url: URL) throws {
        let size = (try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.uint64Value ?? 0
        self.init(name: url.lastPathComponent, size: size, mimeType: TransferPlan.mimeType(forExtension: url.pathExtension), url: url)
    }
}
