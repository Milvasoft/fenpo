namespace Fenpo.Core.Transfers;

/// <summary>
/// The MIME type a file offer carries, from the file extension. The phone uses it to pick where a file goes (photos,
/// videos, downloads); anything unknown is plain bytes.
/// </summary>
public static class MimeTypes
{
    private const string _bytes = "application/octet-stream";

    private static readonly Dictionary<string, string> _byExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".bmp"] = "image/bmp",
        [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".m4v"] = "video/x-m4v",
        [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm",
        [".avi"] = "video/x-msvideo",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".wav"] = "audio/wav",
        [".flac"] = "audio/flac",
        [".ogg"] = "audio/ogg",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".rar"] = "application/vnd.rar",
        [".apk"] = "application/vnd.android.package-archive",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    /// <summary>
    /// Gets the MIME type for a file.
    /// </summary>
    /// <param name="path">File path or name.</param>
    /// <returns>The type, or <c>application/octet-stream</c>.</returns>
    public static string Of(string path) => _byExtension.GetValueOrDefault(Path.GetExtension(path), _bytes);
}
