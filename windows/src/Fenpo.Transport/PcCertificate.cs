using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Fenpo.Transport;

/// <summary>
/// The PC's TLS identity: an ECDSA P-256 self-signed certificate made on first run, kept as a PFX protected with DPAPI
/// for the current user. Phones pin its SHA-256 from the QR code (ARCHITECTURE "Eşleşme ve güvenlik").
/// </summary>
public sealed class PcCertificate : IDisposable
{
    private static readonly byte[] _entropy = "fenpo-pc-certificate"u8.ToArray();

    private PcCertificate(X509Certificate2 certificate)
    {
        Certificate = certificate;
        Fingerprint = SHA256.HashData(certificate.RawData);
    }

    /// <summary>
    /// Gets the certificate with its private key.
    /// </summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>
    /// Gets the SHA-256 of the certificate (DER), as in the QR code's <c>fp</c>.
    /// </summary>
    public byte[] Fingerprint { get; }

    /// <summary>
    /// Loads the certificate, or makes and saves a new one.
    /// </summary>
    /// <param name="path">PFX file (DPAPI protected).</param>
    /// <param name="commonName">Subject name for a new certificate (the PC name).</param>
    /// <returns>The identity.</returns>
    public static PcCertificate LoadOrCreate(string path, string commonName)
    {
        if (File.Exists(path))
        {
            try
            {
                return new PcCertificate(Load(ProtectedData.Unprotect(File.ReadAllBytes(path), _entropy, DataProtectionScope.CurrentUser)));
            }
            catch (CryptographicException)
            {
                // Another user's or machine's file: phones paired with it have to pair again either way.
            }
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], critical: false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        var pfx = created.Export(X509ContentType.Pfx);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ProtectedData.Protect(pfx, _entropy, DataProtectionScope.CurrentUser));
        return new PcCertificate(Load(pfx));
    }

    /// <inheritdoc/>
    public void Dispose() => Certificate.Dispose();

    // Schannel needs a key it can reach from LSASS, so not EphemeralKeySet (S5).
    private static X509Certificate2 Load(byte[] pfx) => X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
}
