import CryptoKit
import FenpoCore
import Foundation
import Security

/// The phone's TLS identity: a P-256 key in the keychain and its self-signed certificate.
public struct DeviceIdentity: @unchecked Sendable {
    /// Key and certificate together, as Network.framework wants them.
    public let identity: SecIdentity

    /// The certificate in DER.
    public let certificateDER: [UInt8]

    /// SHA-256 of the certificate, the value the PC pins.
    public var fingerprint: [UInt8] {
        CertificateFingerprint.sha256(certificateDER)
    }
}

/// SHA-256 fingerprints of certificates.
public enum CertificateFingerprint {
    /// Hashes a DER certificate.
    /// - Parameter der: Certificate bytes.
    public static func sha256(_ der: some DataProtocol) -> [UInt8] {
        Array(SHA256.hash(data: Array(der)))
    }

    /// Hashes a certificate.
    /// - Parameter certificate: Certificate to hash.
    public static func sha256(_ certificate: SecCertificate) -> [UInt8] {
        sha256(SecCertificateCopyData(certificate) as Data)
    }
}

/// Why the identity could not be made or loaded.
public enum DeviceIdentityError: Error, Equatable, Sendable {
    case keyGenerationFailed(String)
    case publicKeyUnavailable
    case signingFailed(String)
    case certificateRejected
    case identityNotFound(OSStatus)
    case keychain(OSStatus)
}

/// Makes the self-signed certificate from a P-256 private key.
public enum CertificateFactory {
    /// Builds and signs a certificate valid from a day ago for 20 years.
    /// - Parameters:
    ///   - privateKey: P-256 private key that signs and whose public half goes in the certificate.
    ///   - commonName: Subject and issuer CN.
    ///   - now: Reference time.
    public static func makeCertificate(privateKey: SecKey, commonName: String, now: Date = .now) throws -> SecCertificate {
        guard
            let publicKey = SecKeyCopyPublicKey(privateKey),
            let publicBytes = SecKeyCopyExternalRepresentation(publicKey, nil) as Data?
        else {
            throw DeviceIdentityError.publicKeyUnavailable
        }

        var serial = [UInt8](repeating: 0, count: 16)
        _ = SecRandomCopyBytes(kSecRandomDefault, serial.count, &serial)
        serial[0] &= 0x7F
        serial[0] |= 0x01

        let builder = SelfSignedCertificate(
            publicKey: Array(publicBytes),
            commonName: commonName,
            serialNumber: serial,
            notBefore: now.addingTimeInterval(-86_400),
            notAfter: now.addingTimeInterval(20 * 365 * 86_400)
        )

        var error: Unmanaged<CFError>?
        guard let signature = SecKeyCreateSignature(privateKey, .ecdsaSignatureMessageX962SHA256, Data(builder.toBeSigned) as CFData, &error) as Data? else {
            throw DeviceIdentityError.signingFailed(error.map { String(describing: $0.takeRetainedValue()) } ?? "unknown")
        }

        guard let certificate = SecCertificateCreateWithData(nil, Data(builder.certificate(signature: Array(signature))) as CFData) else {
            throw DeviceIdentityError.certificateRejected
        }
        return certificate
    }
}

/// Keeps the identity in the shared keychain group so the share extension can use it too.
public struct DeviceIdentityStore: Sendable {
    private let keychain: Keychain
    private let label: String

    /// Creates a store.
    /// - Parameters:
    ///   - keychain: Keychain group to use.
    ///   - label: Keychain label of the key and certificate; tests use their own.
    public init(keychain: Keychain = .shared, label: String = "com.fenpo.identity") {
        self.keychain = keychain
        self.label = label
    }

    /// Returns the stored identity, creating it on first use.
    /// - Parameter commonName: CN for a new certificate.
    public func loadOrCreate(commonName: String = "Fenpo iPhone") throws -> DeviceIdentity {
        if let identity = try load() {
            return identity
        }
        try create(commonName: commonName)
        guard let identity = try load() else {
            throw DeviceIdentityError.identityNotFound(errSecItemNotFound)
        }
        return identity
    }

    /// Removes the key and certificate.
    public func delete() throws {
        for itemClass in [kSecClassCertificate, kSecClassKey] {
            let query = keychain.scoped([kSecClass as String: itemClass, kSecAttrLabel as String: label])
            let status = SecItemDelete(query as CFDictionary)
            guard status == errSecSuccess || status == errSecItemNotFound else {
                throw DeviceIdentityError.keychain(status)
            }
        }
    }

    private func load() throws -> DeviceIdentity? {
        let query = keychain.scoped([
            kSecClass as String: kSecClassIdentity,
            kSecAttrLabel as String: label,
            kSecReturnRef as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ])
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status != errSecItemNotFound else {
            return nil
        }
        guard status == errSecSuccess, let result, CFGetTypeID(result) == SecIdentityGetTypeID() else {
            throw DeviceIdentityError.identityNotFound(status)
        }
        let identity = result as! SecIdentity  // swiftlint:disable:this force_cast
        var certificate: SecCertificate?
        let copyStatus = SecIdentityCopyCertificate(identity, &certificate)
        guard copyStatus == errSecSuccess, let certificate else {
            throw DeviceIdentityError.identityNotFound(copyStatus)
        }
        return DeviceIdentity(identity: identity, certificateDER: Array(SecCertificateCopyData(certificate) as Data))
    }

    private func create(commonName: String) throws {
        try delete()

        let keyAttributes: [String: Any] = keychain.scoped([
            kSecAttrIsPermanent as String: true,
            kSecAttrApplicationTag as String: Data((label + ".key").utf8),
            kSecAttrLabel as String: label,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
        ])
        let attributes: [String: Any] = [
            kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom,
            kSecAttrKeySizeInBits as String: 256,
            kSecPrivateKeyAttrs as String: keyAttributes,
        ]
        var error: Unmanaged<CFError>?
        guard let privateKey = SecKeyCreateRandomKey(attributes as CFDictionary, &error) else {
            throw DeviceIdentityError.keyGenerationFailed(error.map { String(describing: $0.takeRetainedValue()) } ?? "unknown")
        }

        let certificate = try CertificateFactory.makeCertificate(privateKey: privateKey, commonName: commonName)
        let item = keychain.scoped([
            kSecClass as String: kSecClassCertificate,
            kSecValueRef as String: certificate,
            kSecAttrLabel as String: label,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
        ])
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw DeviceIdentityError.keychain(status)
        }
    }
}
