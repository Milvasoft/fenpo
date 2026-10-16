import Foundation

/// Builds the phone's self-signed ECDSA P-256 certificate (DECISIONS #12) without any Apple framework.
///
/// The caller signs `toBeSigned` with the private key (ECDSA with SHA-256, DER signature) and
/// passes the signature to `certificate(signature:)`.
public struct SelfSignedCertificate: Sendable {
    /// OID 1.2.840.10045.2.1, id-ecPublicKey.
    static let ecPublicKey = DER.objectIdentifier([1, 2, 840, 10045, 2, 1])

    /// OID 1.2.840.10045.3.1.7, prime256v1.
    static let prime256v1 = DER.objectIdentifier([1, 2, 840, 10045, 3, 1, 7])

    /// OID 1.2.840.10045.4.3.2, ecdsa-with-SHA256.
    static let ecdsaWithSHA256 = DER.objectIdentifier([1, 2, 840, 10045, 4, 3, 2])

    /// OID 2.5.4.3, commonName.
    static let commonName = DER.objectIdentifier([2, 5, 4, 3])

    /// DER of the TBSCertificate, the bytes to sign.
    public let toBeSigned: [UInt8]

    /// Prepares a certificate.
    /// - Parameters:
    ///   - publicKey: Uncompressed P-256 point (65 bytes, `0x04 || X || Y`).
    ///   - commonName: Subject and issuer CN.
    ///   - serialNumber: Random serial, up to 20 bytes.
    ///   - notBefore: Start of validity.
    ///   - notAfter: End of validity.
    public init(publicKey: [UInt8], commonName: String, serialNumber: [UInt8], notBefore: Date, notAfter: Date) {
        precondition(publicKey.count == 65 && publicKey.first == 0x04, "Expected an uncompressed P-256 point")
        let name = DER.sequence(DER.set(DER.sequence(Self.commonName, DER.utf8String(commonName))))
        toBeSigned = DER.sequence(
            DER.contextExplicit(0, DER.integer(2)),
            DER.integer(serialNumber),
            DER.sequence(Self.ecdsaWithSHA256),
            name,
            DER.sequence(DER.time(notBefore), DER.time(notAfter)),
            name,
            DER.sequence(DER.sequence(Self.ecPublicKey, Self.prime256v1), DER.bitString(publicKey))
        )
    }

    /// The finished certificate in DER.
    /// - Parameter signature: DER ECDSA signature (`Ecdsa-Sig-Value`) over `toBeSigned`.
    public func certificate(signature: [UInt8]) -> [UInt8] {
        DER.sequence(toBeSigned, DER.sequence(Self.ecdsaWithSHA256), DER.bitString(signature))
    }
}
