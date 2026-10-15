using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Fenpo.Spikes.S5;

/// <summary>
/// A device identity: a stable id and a self-signed ECDSA P-256 certificate, kept on disk with DPAPI.
/// </summary>
public sealed class Identity
{
    private Identity(string id, X509Certificate2 certificate)
    {
        Id = id;
        Certificate = certificate;
    }

    public string Id { get; }

    public X509Certificate2 Certificate { get; }

    public string Fingerprint => Fingerprints.Of(Certificate);

    /// <summary>
    /// Loads the identity from <paramref name="directory"/> or creates it on first use.
    /// </summary>
    public static Identity LoadOrCreate(string directory, string commonName)
    {
        Directory.CreateDirectory(directory);
        var idPath = Path.Combine(directory, "id.txt");
        var certificatePath = Path.Combine(directory, "identity.pfx.dpapi");

        if (File.Exists(idPath) && File.Exists(certificatePath))
        {
            var pfx = ProtectedData.Unprotect(File.ReadAllBytes(certificatePath), null, DataProtectionScope.CurrentUser);
            return new Identity(File.ReadAllText(idPath).Trim(), Load(pfx));
        }

        var id = Guid.NewGuid().ToString();
        var created = Create(commonName);
        File.WriteAllText(idPath, id);
        File.WriteAllBytes(certificatePath, ProtectedData.Protect(created, null, DataProtectionScope.CurrentUser));
        return new Identity(id, Load(created));
    }

    /// <summary>
    /// Creates a throwaway identity that is never saved (for negative tests).
    /// </summary>
    public static Identity CreateEphemeral(string commonName) => new(Guid.NewGuid().ToString(), Load(Create(commonName)));

    private static byte[] Create(string commonName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2")], false));

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return certificate.Export(X509ContentType.Pkcs12);
    }

    // Schannel needs a key it can reach from LSASS, so the PFX is imported without EphemeralKeySet.
    private static X509Certificate2 Load(byte[] pfx) => X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
}

/// <summary>
/// SHA-256 certificate fingerprints as lowercase hex, the form used in the pairing QR code.
/// </summary>
public static class Fingerprints
{
    public static string Of(X509Certificate certificate) => Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData()));
}
