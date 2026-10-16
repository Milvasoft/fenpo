import FenpoCore
import Foundation
import Testing

struct LogFileTests {
    func makeLog(maxBytes: Int = 4_096) -> LogFile {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("fenpo-log-\(UUID().uuidString)")
        return LogFile(directory: directory, maxBytes: maxBytes)
    }

    @Test
    func linesCarryTimeLevelAndCategory() {
        let log = makeLog()
        let line = log.line(date: Date(timeIntervalSince1970: 1_792_108_800), level: .info, category: "BLE", message: "two\nlines")
        #expect(line == "2026-10-16T00:00:00.000Z INFO [BLE] two lines\n")
    }

    @Test
    func appendsAndReadsInOrder() {
        let log = makeLog()
        defer { log.clear() }
        log.append(.info, category: "App", "first")
        log.append(.error, category: "BLE", "second")
        let lines = log.read().split(separator: "\n")
        #expect(lines.count == 2)
        #expect(lines[0].hasSuffix("INFO [App] first"))
        #expect(lines[1].hasSuffix("ERROR [BLE] second"))
    }

    @Test
    func rotatesAndKeepsThePreviousFile() {
        let log = makeLog(maxBytes: 200)
        defer { log.clear() }
        for index in 0..<20 {
            log.append(.info, category: "T", "line \(index)")
        }
        #expect(FileManager.default.fileExists(atPath: log.previousURL.path))
        let lines = log.read().split(separator: "\n")
        #expect(lines.last?.hasSuffix("line 19") == true)
        #expect(lines.count < 20)
        // Oldest kept line comes first.
        let numbers = lines.compactMap { Int($0.split(separator: " ").last ?? "") }
        #expect(numbers == numbers.sorted())
    }

    @Test
    func clearRemovesEverything() {
        let log = makeLog()
        log.append(.info, category: "T", "x")
        log.clear()
        #expect(log.read().isEmpty)
    }
}
