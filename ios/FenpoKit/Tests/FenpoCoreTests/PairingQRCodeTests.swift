import FenpoCore
import Testing

struct PairingQRCodeTests {
    static let fingerprint = String(repeating: "ab", count: 32)
    static let token = Base64URL.encode(Array(0..<16))

    static func link(
        v: String = "1",
        id: String = "pc-7f3a",
        n: String = "BUGRA%2DPC%20%C3%87al%C4%B1%C5%9Fma",
        a: String = "192.168.1.20:52000,[fe80::1%25en0]:52000",
        fp: String = fingerprint,
        t: String = token
    ) -> String {
        "fenpo://pair?v=\(v)&id=\(id)&n=\(n)&a=\(a)&fp=\(fp)&t=\(t)"
    }

    @Test
    func parsesTheDocumentedFormat() throws {
        let code = try PairingQRCode(parsing: Self.link())
        #expect(code.pcID == "pc-7f3a")
        #expect(code.pcName == "BUGRA-PC Çalışma")
        #expect(code.endpoints == [
            Endpoint(host: "192.168.1.20", port: 52000),
            Endpoint(host: "fe80::1%en0", port: 52000),
        ])
        #expect(code.certificateSHA256 == [UInt8](repeating: 0xAB, count: 32))
        #expect(code.token == Array(0..<16))
    }

    @Test
    func acceptsUpperCaseFingerprintAndSurroundingWhitespace() throws {
        let code = try PairingQRCode(parsing: "  " + Self.link(fp: Self.fingerprint.uppercased()) + "\n")
        #expect(code.certificateSHA256.count == 32)
    }

    @Test(arguments: [
        ("https://example.com/?v=1", PairingQRCode.ParseError.notPairingLink),
        ("fenpo://other?v=1", .notPairingLink),
        ("not a link", .notPairingLink),
    ])
    func rejectsOtherCodes(text: String, error: PairingQRCode.ParseError) {
        #expect(throws: error) {
            try PairingQRCode(parsing: text)
        }
    }

    @Test
    func rejectsNewerVersions() {
        #expect(throws: PairingQRCode.ParseError.unsupportedVersion("2")) {
            try PairingQRCode(parsing: Self.link(v: "2"))
        }
    }

    @Test
    func rejectsMissingName() {
        #expect(throws: PairingQRCode.ParseError.missingField("n")) {
            try PairingQRCode(parsing: Self.link(n: ""))
        }
    }

    @Test(arguments: ["192.168.1.20", "192.168.1.20:0", "fe80::1:52000", "[fe80::1]", "host:99999"])
    func rejectsBadAddresses(address: String) {
        #expect(throws: PairingQRCode.ParseError.invalidAddress(address)) {
            try PairingQRCode(parsing: Self.link(a: address))
        }
    }

    @Test(arguments: ["abcd", String(repeating: "zz", count: 32), String(repeating: "ab", count: 33)])
    func rejectsBadFingerprints(fingerprint: String) {
        #expect(throws: PairingQRCode.ParseError.invalidFingerprint) {
            try PairingQRCode(parsing: Self.link(fp: fingerprint))
        }
    }

    @Test(arguments: [Base64URL.encode(Array(0..<15)), "a+b/", "%%%"])
    func rejectsBadTokens(token: String) {
        #expect(throws: PairingQRCode.ParseError.invalidToken) {
            try PairingQRCode(parsing: Self.link(t: token))
        }
    }

    @Test
    func endpointDescriptionBracketsIPv6() {
        #expect(Endpoint(host: "fe80::1", port: 1).description == "[fe80::1]:1")
        #expect(Endpoint(host: "10.0.0.2", port: 52000).description == "10.0.0.2:52000")
    }

    @Test
    func hexAndBase64URLRoundTrip() {
        let bytes: [UInt8] = [0, 1, 0x7F, 0x80, 0xFE, 0xFF]
        #expect(Hex.encode(bytes) == "00017f80feff")
        #expect(Hex.decode("00017F80FEFF") == bytes)
        #expect(Base64URL.decode(Base64URL.encode(bytes)) == bytes)
        #expect(!Base64URL.encode([0xFB, 0xFF]).contains("="))
    }
}
