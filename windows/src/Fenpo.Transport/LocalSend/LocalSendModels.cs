using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fenpo.Transport.LocalSend;

/// <summary>
/// A device as LocalSend v2 describes itself (multicast announcement, /register, /info, prepare-upload's info).
/// </summary>
public sealed record LocalSendDevice
{
    /// <summary>Gets the name shown to users.</summary>
    public string Alias { get; init; } = string.Empty;

    /// <summary>Gets the protocol version, "2.1".</summary>
    public string Version { get; init; } = "2.1";

    /// <summary>Gets the model, e.g. "Windows" or "iPhone".</summary>
    public string? DeviceModel { get; init; }

    /// <summary>Gets the kind: mobile, desktop, web, headless or server.</summary>
    public string? DeviceType { get; init; }

    /// <summary>Gets the SHA-256 of the device's TLS certificate (hex), or a random id over HTTP.</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>Gets the HTTP(S) port.</summary>
    public int Port { get; init; } = LocalSendServer.DefaultPort;

    /// <summary>Gets "https" or "http".</summary>
    public string Protocol { get; init; } = "https";

    /// <summary>Gets a value indicating whether the device offers files to download from a browser.</summary>
    public bool Download { get; init; }

    /// <summary>Gets a value indicating whether a multicast message asks others to answer.</summary>
    public bool? Announce { get; init; }
}

/// <summary>
/// One file in a prepare-upload request.
/// </summary>
public sealed record LocalSendFile
{
    /// <summary>Gets the sender's id for the file.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the file name, possibly with a relative folder.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Gets the size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Gets the MIME type.</summary>
    public string? FileType { get; init; }

    /// <summary>Gets the SHA-256 (hex), when the sender gives it.</summary>
    public string? Sha256 { get; init; }
}

/// <summary>
/// The body of POST /api/localsend/v2/prepare-upload.
/// </summary>
public sealed record LocalSendPrepareUpload
{
    /// <summary>Gets the sender.</summary>
    public LocalSendDevice Info { get; init; } = new();

    /// <summary>Gets the files by id.</summary>
    public Dictionary<string, LocalSendFile> Files { get; init; } = [];
}

/// <summary>
/// A sender's request to send files, as the PC's user is asked about it.
/// </summary>
/// <param name="SessionId">LocalSend session id.</param>
/// <param name="Sender">Who sends.</param>
/// <param name="Files">What.</param>
public sealed record LocalSendOffer(string SessionId, LocalSendDevice Sender, IReadOnlyList<LocalSendFile> Files)
{
    /// <summary>
    /// Gets the total size in bytes.
    /// </summary>
    public long TotalBytes => Files.Sum(f => f.Size);
}

/// <summary>
/// JSON settings LocalSend uses: camelCase, nulls left out.
/// </summary>
internal static class LocalSendJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
