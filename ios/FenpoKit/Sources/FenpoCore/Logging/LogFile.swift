import Foundation

/// A small rotating text log on disk.
///
/// `os.Logger` cannot show earlier processes, but the device tests need what happened during
/// background wakes and before a force quit, so every log line is also written here.
public final class LogFile: @unchecked Sendable {
    /// Severity of a line.
    public enum Level: String, Sendable {
        case debug = "DEBUG"
        case info = "INFO"
        case error = "ERROR"
    }

    private let directory: URL
    private let baseName: String
    private let maxBytes: Int
    private let lock = NSLock()
    private let formatter: ISO8601DateFormatter

    /// Creates a log in a directory.
    /// - Parameters:
    ///   - directory: Folder for the files; created when missing.
    ///   - baseName: File name without extension.
    ///   - maxBytes: Size at which the current file becomes the previous one.
    public init(directory: URL, baseName: String = "fenpo", maxBytes: Int = 512 * 1024) {
        self.directory = directory
        self.baseName = baseName
        self.maxBytes = maxBytes
        formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    }

    /// File being written.
    public var currentURL: URL {
        directory.appendingPathComponent(baseName + ".log")
    }

    /// File written before the last rotation.
    public var previousURL: URL {
        directory.appendingPathComponent(baseName + ".1.log")
    }

    /// Formats one line.
    /// - Parameters:
    ///   - date: Time of the event.
    ///   - level: Severity.
    ///   - category: Area, e.g. "BLE".
    ///   - message: Text; line breaks are flattened.
    public func line(date: Date, level: Level, category: String, message: String) -> String {
        let flat = message.replacingOccurrences(of: "\r", with: " ").replacingOccurrences(of: "\n", with: " ")
        return "\(formatter.string(from: date)) \(level.rawValue) [\(category)] \(flat)\n"
    }

    /// Appends a line, rotating first when the file is full.
    /// - Parameters:
    ///   - level: Severity.
    ///   - category: Area, e.g. "BLE".
    ///   - message: Text.
    ///   - date: Time of the event.
    public func append(_ level: Level, category: String, _ message: String, date: Date = Date()) {
        lock.lock()
        defer {
            lock.unlock()
        }
        let data = Data(line(date: date, level: level, category: category, message: message).utf8)
        let fileManager = FileManager.default
        try? fileManager.createDirectory(at: directory, withIntermediateDirectories: true)

        if let size = (try? fileManager.attributesOfItem(atPath: currentURL.path))?[.size] as? Int, size + data.count > maxBytes {
            try? fileManager.removeItem(at: previousURL)
            try? fileManager.moveItem(at: currentURL, to: previousURL)
        }

        if let handle = try? FileHandle(forWritingTo: currentURL) {
            defer {
                try? handle.close()
            }
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: data)
        } else {
            try? data.write(to: currentURL)
        }
    }

    /// Previous and current file, oldest line first.
    public func read() -> String {
        lock.lock()
        defer {
            lock.unlock()
        }
        let previous = (try? String(contentsOf: previousURL, encoding: .utf8)) ?? ""
        let current = (try? String(contentsOf: currentURL, encoding: .utf8)) ?? ""
        return previous + current
    }

    /// Deletes both files.
    public func clear() {
        lock.lock()
        defer {
            lock.unlock()
        }
        try? FileManager.default.removeItem(at: currentURL)
        try? FileManager.default.removeItem(at: previousURL)
    }
}
