import CryptoKit
@testable import FenpoApple
import FenpoCore
import Foundation
import Security
import Testing

struct CertificateFactoryTests {
    /// A P-256 key that only lives in memory, so the test needs no keychain entitlement.
    func makeEphemeralKey() throws -> SecKey {
        let attributes: [String: Any] = [
            kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom,
            kSecAttrKeySizeInBits as String: 256,
        ]
        var error: Unmanaged<CFError>?
        return try #require(SecKeyCreateRandomKey(attributes as CFDictionary, &error))
    }

    @Test
    func securityFrameworkAcceptsTheCertificate() throws {
        let privateKey = try makeEphemeralKey()
        let certificate = try CertificateFactory.makeCertificate(privateKey: privateKey, commonName: "Fenpo iPhone")

        #expect(SecCertificateCopySubjectSummary(certificate) as String? == "Fenpo iPhone")

        let certificateKey = try #require(SecCertificateCopyKey(certificate))
        let expectedKey = try #require(SecKeyCopyPublicKey(privateKey))
        #expect(SecKeyCopyExternalRepresentation(certificateKey, nil) as Data? == SecKeyCopyExternalRepresentation(expectedKey, nil) as Data?)
    }

    @Test
    func certificateSignatureVerifiesWithItsOwnKey() throws {
        let privateKey = try makeEphemeralKey()
        let certificate = try CertificateFactory.makeCertificate(privateKey: privateKey, commonName: "Fenpo iPhone")
        let der = Array(SecCertificateCopyData(certificate) as Data)

        // Certificate ::= SEQUENCE { tbsCertificate, signatureAlgorithm, signatureValue BIT STRING }
        let parts = try DERParts(der)
        let publicKey = try #require(SecKeyCopyPublicKey(privateKey))
        var error: Unmanaged<CFError>?
        let valid = SecKeyVerifySignature(
            publicKey,
            .ecdsaSignatureMessageX962SHA256,
            Data(parts.tbs) as CFData,
            Data(parts.signature) as CFData,
            &error
        )
        #expect(valid)
    }

    @Test
    func trustAcceptsTheSelfSignedCertificateAsItsOwnAnchor() throws {
        let certificate = try CertificateFactory.makeCertificate(privateKey: try makeEphemeralKey(), commonName: "Fenpo iPhone")
        var trust: SecTrust?
        #expect(SecTrustCreateWithCertificates(certificate, SecPolicyCreateBasicX509(), &trust) == errSecSuccess)
        let secTrust = try #require(trust)
        #expect(SecTrustSetAnchorCertificates(secTrust, [certificate] as CFArray) == errSecSuccess)
        var error: CFError?
        #expect(SecTrustEvaluateWithError(secTrust, &error), "\(String(describing: error))")
    }

    @Test
    func fingerprintIsSHA256OfTheDER() throws {
        let certificate = try CertificateFactory.makeCertificate(privateKey: try makeEphemeralKey(), commonName: "Fenpo iPhone")
        let der = SecCertificateCopyData(certificate) as Data
        #expect(CertificateFingerprint.sha256(certificate) == Array(SHA256.hash(data: der)))
        #expect(CertificateFingerprint.sha256(certificate).count == 32)
    }
}

struct AESGCMChannelCipherTests {
    let key = [UInt8](repeating: 0x42, count: 32)

    @Test
    func phoneAndPCRoundTrip() throws {
        var phone = try AESGCMChannelCipher(key: key, sender: .phone)
        var pc = try AESGCMChannelCipher(key: key, sender: .pc)

        let message: [UInt8] = Array("hello pc".utf8)
        let sealed = try phone.seal(message)
        #expect(sealed.count == BLEChannelNonce.length + message.count + 16)
        #expect(Array(sealed.prefix(BLEChannelNonce.length)) == BLEChannelNonce.make(sender: .phone, counter: 1))
        #expect(try pc.open(sealed) == message)

        let reply = try pc.seal(Array("hello phone".utf8))
        #expect(try phone.open(reply) == Array("hello phone".utf8))
        #expect(phone.counters == BLEChannelCounters(lastSent: 1, lastReceived: 1))
    }

    @Test
    func rejectsReplaysTamperingAndReflection() throws {
        var phone = try AESGCMChannelCipher(key: key, sender: .phone)
        var pc = try AESGCMChannelCipher(key: key, sender: .pc)
        let sealed = try phone.seal([1, 2, 3])
        _ = try pc.open(sealed)

        #expect(throws: BLEChannelNonce.Error.replayed(counter: 1, lastAccepted: 1)) {
            try pc.open(sealed)
        }

        var tampered = try phone.seal([4, 5, 6])
        tampered[tampered.count - 1] ^= 0xFF
        #expect(throws: (any Error).self) {
            try pc.open(tampered)
        }
        // A failed open does not move the replay window.
        #expect(pc.counters.lastReceived == 1)

        // A message the phone sealed must not be accepted by the phone itself.
        let own = try phone.seal([7])
        #expect(throws: BLEChannelNonce.Error.unexpectedSender(0x01)) {
            try phone.open(own)
        }
    }

    @Test
    func countersContinueAfterRestore() throws {
        var first = try AESGCMChannelCipher(key: key, sender: .phone)
        _ = try first.seal([1])
        _ = try first.seal([2])
        var restored = try AESGCMChannelCipher(key: key, sender: .phone, counters: first.counters)
        let sealed = try restored.seal([3])
        #expect(Array(sealed.prefix(BLEChannelNonce.length)) == BLEChannelNonce.make(sender: .phone, counter: 3))
    }

    @Test
    func rejectsShortKeys() {
        #expect(throws: AESGCMChannelCipher.Error.invalidKeyLength(16)) {
            try AESGCMChannelCipher(key: [UInt8](repeating: 0, count: 16))
        }
    }
}

/// Splits a DER certificate into the signed bytes and the raw signature.
struct DERParts {
    let tbs: [UInt8]
    let signature: [UInt8]

    init(_ der: [UInt8]) throws {
        var offset = 0
        let outer = try Self.element(der, at: &offset)
        var inner = outer.contentStart
        let tbsStart = inner
        let tbs = try Self.element(der, at: &inner)
        self.tbs = Array(der[tbsStart..<tbs.end])
        _ = try Self.element(der, at: &inner)
        let bitString = try Self.element(der, at: &inner)
        signature = Array(der[(bitString.contentStart + 1)..<bitString.end])
    }

    private static func element(_ bytes: [UInt8], at offset: inout Int) throws -> (contentStart: Int, end: Int) {
        var index = offset + 1
        var length = Int(bytes[index])
        index += 1
        if length & 0x80 != 0 {
            let count = length & 0x7F
            length = bytes[index..<(index + count)].reduce(0) { $0 << 8 | Int($1) }
            index += count
        }
        offset = index + length
        return (index, index + length)
    }
}
