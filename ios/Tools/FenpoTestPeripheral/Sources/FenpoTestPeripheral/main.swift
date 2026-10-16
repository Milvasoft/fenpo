import FenpoCore
import Foundation

// Plays the PC on the Fenpo BLE channel for device tests (docs/ios/TESTING.md).
// Commands are read from standard input; type `help`.

let server = TestPeripheralServer()
server.start()
print(TestPeripheralServer.helpText)

Thread.detachNewThread {
    while let line = readLine() {
        Task { @MainActor in
            server.handle(command: line)
        }
    }
    exit(0)
}

dispatchMain()
