#if os(iOS)
import CryptoKit
import FenpoCore
import FenpoProtocol
import Foundation
import UserNotifications

/// File transfers in both directions over the Wi-Fi data channel (protocol/README.md, "File transfer").
/// The phone opens every data connection. Also used by the share extension with its own channel.
@MainActor
public final class FileTransferService {
    /// Transfers in progress, by transfer id.
    public private(set) var active: [String: ActiveTransfer] = [:] {
        didSet {
            onChange?()
        }
    }

    /// Called when a transfer starts, moves or ends.
    public var onChange: (() -> Void)?

    private let wifi: WiFiChannel
    private let store: TransferStore
    private var cancelled: Set<String> = []
    private let log = FenpoLog("Transfer")

    private static let chunkSize = 256 * 1024
    private static let reportEvery: UInt64 = 1024 * 1024

    /// Creates the service.
    /// - Parameters:
    ///   - wifi: Control channel the negotiation runs on.
    ///   - store: History.
    public init(wifi: WiFiChannel, store: TransferStore = TransferStore()) {
        self.wifi = wifi
        self.store = store
    }

    // MARK: - Phone to PC

    /// Offers files to the PC and streams them once accepted.
    /// - Parameter files: Files in order.
    public func send(_ files: [OutgoingFile]) async throws {
        guard let first = files.first else {
            throw FenpoActionError.emptyText
        }
        try await wifi.connectIfNeeded()
        guard wifi.session?.supports(.fileTransfer) == true else {
            throw FenpoActionError.unsupportedByPC
        }

        let id = UUID().uuidString
        let offer = TransferPlan.offer(transferID: id, files: files)
        active[id] = ActiveTransfer(id: id, firstFileName: first.name, fileCount: files.count, direction: .toPC, totalBytes: offer.totalBytes)
        defer {
            active[id] = nil
        }
        log.info("Offering \(files.count) file(s), \(offer.totalBytes) bytes")

        let wifi = wifi
        let acceptWait = Task {
            try await wifi.next(timeout: .seconds(120)) { envelope in
                if case .transferAccept(let accept) = envelope.body {
                    return accept.transferID == id
                }
                return false
            }
        }
        try await wifi.send(.transferOffer(offer))
        guard case .transferAccept(let accept) = try await acceptWait.value.body, accept.accepted else {
            record(files.map(\.name), sizes: files.map(\.size), direction: .toPC, outcome: .cancelled)
            throw FenpoActionError.declinedByPC
        }

        let seconds = max(120, Int(offer.totalBytes / 500_000))
        let resultWait = Task {
            try await wifi.next(timeout: .seconds(seconds)) { envelope in
                if case .transferProgress(let progress) = envelope.body {
                    return progress.transferID == id && progress.state != .inProgress && progress.state != .unspecified
                }
                return false
            }
        }

        let stream = try await wifi.openStream()
        var recorded = false
        do {
            try await stream.sendRaw(Data(try TransferPlan.headerFrame(transferID: id)))
            var written: UInt64 = 0
            for file in files {
                let handle = try FileHandle(forReadingFrom: file.url)
                defer {
                    try? handle.close()
                }
                while let chunk = try handle.read(upToCount: Self.chunkSize), !chunk.isEmpty {
                    if cancelled.contains(id) {
                        throw FenpoActionError.transferFailed
                    }
                    try await stream.sendRaw(chunk)
                    written += UInt64(chunk.count)
                    active[id]?.bytesDone = written
                }
                active[id]?.filesDone += 1
            }
            let result = try await resultWait.value
            await stream.drain(timeout: .seconds(3))
            await stream.cancel()
            let completed: Bool
            if case .transferProgress(let progress) = result.body, progress.state == .completed {
                completed = true
            } else {
                completed = false
            }
            record(files.map(\.name), sizes: files.map(\.size), direction: .toPC, outcome: completed ? .completed : .failed)
            recorded = true
            guard completed else {
                throw FenpoActionError.transferFailed
            }
            log.info("Sent \(files.count) file(s)")
        } catch {
            resultWait.cancel()
            await stream.cancel()
            if !recorded {
                record(files.map(\.name), sizes: files.map(\.size), direction: .toPC, outcome: .failed)
            }
            log.error("Sending failed: \(String(describing: error))")
            throw error is FenpoActionError ? error : FenpoActionError.transferFailed
        }
    }

    // MARK: - PC to phone

    /// Handles transfer messages from the PC; returns whether it took the message.
    /// - Parameter envelope: Message from the control channel.
    @discardableResult
    public func handle(_ envelope: Fenpo_V1_Envelope) -> Bool {
        switch envelope.body {
        case .transferOffer(let offer):
            Task {
                await receive(offer)
            }
            return true
        case .transferProgress(let progress):
            if active[progress.transferID]?.direction == .toPC, progress.state == .inProgress {
                // The PC counts what it received; prefer it to what was written into the socket.
                active[progress.transferID]?.bytesDone = progress.bytesDone
            }
            return true
        case .transferCancel(let cancel):
            cancelled.insert(cancel.transferID)
            return true
        default:
            return false
        }
    }

    private func receive(_ offer: Fenpo_V1_TransferOffer) async {
        let id = offer.transferID
        let files = offer.files
        guard !files.isEmpty else {
            return
        }
        log.info("PC offers \(files.count) file(s), \(offer.totalBytes) bytes")
        active[id] = ActiveTransfer(id: id, firstFileName: files[0].name, fileCount: files.count, direction: .fromPC, totalBytes: offer.totalBytes)
        defer {
            active[id] = nil
            cancelled.remove(id)
        }

        let inbox = TransferStore.inboxURL
        var taken = Set((try? FileManager.default.contentsOfDirectory(atPath: inbox.path)) ?? [])
        let names = files.map { file in
            let name = TransferPlan.safeFileName(file.name, existing: taken)
            taken.insert(name)
            return name
        }
        let urls = names.map { inbox.appendingPathComponent($0) }

        var stream: TLSControlConnection?
        do {
            var accept = Fenpo_V1_TransferAccept()
            accept.transferID = id
            accept.accepted = true
            try await wifi.send(.transferAccept(accept))

            let connection = try await wifi.openStream()
            stream = connection
            try await connection.sendRaw(Data(try TransferPlan.headerFrame(transferID: id)))

            try FileManager.default.createDirectory(at: inbox, withIntermediateDirectories: true)
            var handles: [FileHandle] = []
            for url in urls {
                FileManager.default.createFile(atPath: url.path, contents: nil)
                handles.append(try FileHandle(forWritingTo: url))
            }
            defer {
                for handle in handles {
                    try? handle.close()
                }
            }
            var hashers = files.map { _ in SHA256() }
            var splitter = IncomingFileSplitter(sizes: files.map(\.size))
            var lastReport: UInt64 = 0

            while !splitter.isComplete {
                if cancelled.contains(id) {
                    throw FenpoActionError.transferFailed
                }
                guard let data = try await connection.receiveRaw() else {
                    throw FenpoActionError.transferFailed
                }
                for piece in splitter.consume(Array(data)) {
                    try handles[piece.fileIndex].write(contentsOf: piece.bytes)
                    hashers[piece.fileIndex].update(data: piece.bytes)
                    if piece.completesFile {
                        active[id]?.filesDone += 1
                    }
                }
                active[id]?.bytesDone = splitter.bytesDone
                if splitter.bytesDone - lastReport >= Self.reportEvery {
                    lastReport = splitter.bytesDone
                    try await report(id, bytes: splitter.bytesDone, state: .inProgress)
                }
            }

            for (index, file) in files.enumerated() where !file.sha256.isEmpty {
                if Array(hashers[index].finalize()) != Array(file.sha256) {
                    log.error("SHA-256 mismatch in file \(index)")
                    throw FenpoActionError.transferFailed
                }
            }
            try await report(id, bytes: splitter.bytesDone, state: .completed)
            await connection.drain(timeout: .seconds(5))
            await connection.cancel()
            store.add(zip(files, names).reversed().map { file, name in
                TransferRecord(fileName: name, direction: .fromPC, date: Date(), outcome: .completed, size: file.size, localFileName: name)
            })
            log.info("Received \(files.count) file(s)")
            notifyReceived(count: files.count, firstName: names[0])
        } catch {
            log.error("Receiving failed: \(String(describing: error))")
            try? await report(id, bytes: active[id]?.bytesDone ?? 0, state: .failed)
            if let stream {
                await stream.cancel()
            }
            for url in urls {
                try? FileManager.default.removeItem(at: url)
            }
            record(names, sizes: files.map(\.size), direction: .fromPC, outcome: .failed)
        }
    }

    private func report(_ id: String, bytes: UInt64, state: Fenpo_V1_TransferState) async throws {
        var progress = Fenpo_V1_TransferProgress()
        progress.transferID = id
        progress.bytesDone = bytes
        progress.state = state
        try await wifi.send(.transferProgress(progress))
    }

    private func record(_ names: [String], sizes: [UInt64], direction: TransferRecord.Direction, outcome: TransferRecord.Outcome) {
        store.add(zip(names, sizes).reversed().map { name, size in
            TransferRecord(fileName: name, direction: direction, date: Date(), outcome: outcome, size: size)
        })
    }

    private func notifyReceived(count: Int, firstName: String) {
        let content = UNMutableNotificationContent()
        content.title = String(localized: "transfer.notification.title")
        content.body = count == 1 ? firstName : String(localized: "transfer.notification.body \(count)")
        let request = UNNotificationRequest(identifier: "com.fenpo.transfer.\(UUID().uuidString)", content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }
}
#endif
