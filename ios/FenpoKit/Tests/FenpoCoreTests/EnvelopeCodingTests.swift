import FenpoProtocol
import Foundation
import Testing

/// Checks that the generated protocol types are present and round-trip through the wire format.
struct EnvelopeCodingTests {
    @Test
    func helloRoundTrips() throws {
        var hello = Fenpo_V1_Hello()
        hello.deviceID = "6E0C2B1A-0D4F-4F7B-9B8E-2F1D3C4B5A69"
        hello.deviceName = "iPhone"
        hello.platform = .ios
        hello.appVersion = "0.1.0"
        hello.protocolVersions = [FenpoProtocolVersion.current]
        hello.capabilities = [.clipboard, .fileTransfer, .ring]

        var envelope = Fenpo_V1_Envelope()
        envelope.version = FenpoProtocolVersion.current
        envelope.id = 1
        envelope.hello = hello

        let bytes: [UInt8] = try envelope.serializedBytes()
        let decoded = try Fenpo_V1_Envelope(serializedBytes: bytes)

        #expect(decoded == envelope)
        #expect(decoded.hello.capabilities == [.clipboard, .fileTransfer, .ring])
    }
}
