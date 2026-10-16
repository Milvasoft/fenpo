@testable import FenpoCore
import Foundation
import Testing

struct DERTests {
    @Test
    func integersStayPositiveAndMinimal() {
        #expect(DER.integer(0) == [0x02, 0x01, 0x00])
        #expect(DER.integer(127) == [0x02, 0x01, 0x7F])
        #expect(DER.integer(128) == [0x02, 0x02, 0x00, 0x80])
        #expect(DER.integer([0x00, 0x00, 0x01, 0x00]) == [0x02, 0x02, 0x01, 0x00])
    }

    @Test
    func objectIdentifiersUseBase128() {
        // 1.2.840.10045.2.1, id-ecPublicKey
        #expect(DER.objectIdentifier([1, 2, 840, 10045, 2, 1]) == [0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01])
    }

    @Test
    func longLengthsUseTheLongForm() {
        let encoded = DER.tlv(0x04, [UInt8](repeating: 0, count: 300))
        #expect(Array(encoded.prefix(4)) == [0x04, 0x82, 0x01, 0x2C])
    }

    @Test
    func timesSwitchToGeneralizedTimeIn2050() throws {
        let utc = DER.time(Date(timeIntervalSince1970: 1_792_108_800))  // 2026-10-16 00:00:00Z
        #expect(utc == [0x17, 13] + Array("261016000000Z".utf8))
        let generalized = DER.time(Date(timeIntervalSince1970: 2_524_608_000))  // 2050-01-01 00:00:00Z
        #expect(generalized == [0x18, 15] + Array("20500101000000Z".utf8))
    }
}

struct SelfSignedCertificateTests {
    let publicKey: [UInt8] = [0x04] + [UInt8](repeating: 0x11, count: 32) + [UInt8](repeating: 0x22, count: 32)

    @Test
    func certificateHasTheX509Shape() throws {
        let builder = SelfSignedCertificate(
            publicKey: publicKey,
            commonName: "Fenpo iPhone",
            serialNumber: [0x8F] + [UInt8](repeating: 1, count: 15),
            notBefore: Date(timeIntervalSince1970: 1_792_108_800),
            notAfter: Date(timeIntervalSince1970: 2_423_260_800)
        )
        let signature = DER.sequence(DER.integer([0x01]), DER.integer([0x02]))
        let certificate = try DERNode.parse(builder.certificate(signature: signature))

        #expect(certificate.tag == 0x30)
        let parts = try certificate.children
        #expect(parts.count == 3)
        #expect(parts[0].content == (try DERNode.parse(builder.toBeSigned)).content)
        #expect(parts[1] == (try DERNode.parse(DER.sequence(SelfSignedCertificate.ecdsaWithSHA256))))
        #expect(parts[2].tag == 0x03)
        #expect(Array(parts[2].content.dropFirst()) == signature)

        let tbs = try parts[0].children
        #expect(tbs.count == 7)
        #expect(tbs[0].tag == 0xA0)  // version [0] v3
        #expect(tbs[1].content.first == 0x00)  // serial kept positive
        #expect(tbs[3] == tbs[5])  // issuer == subject
        let validity = try tbs[4].children
        #expect(validity.map(\.tag) == [0x17, 0x17])

        let keyInfo = try tbs[6].children
        #expect(try keyInfo[0].children == [
            try DERNode.parse(SelfSignedCertificate.ecPublicKey),
            try DERNode.parse(SelfSignedCertificate.prime256v1),
        ])
        #expect(Array(keyInfo[1].content.dropFirst()) == publicKey)

        let commonName = try tbs[5].children[0].children[0].children[1]
        #expect(commonName.tag == 0x0C)
        #expect(String(decoding: commonName.content, as: UTF8.self) == "Fenpo iPhone")
    }
}
