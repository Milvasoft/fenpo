using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Fenpo.Transport.LocalSend;

/// <summary>
/// One HTTP/1.1 request: method, path without the query, query values, headers and how its body comes.
/// </summary>
/// <param name="Method">GET, POST.</param>
/// <param name="Path">Path, e.g. "/api/localsend/v2/upload".</param>
/// <param name="Query">Decoded query values.</param>
/// <param name="ContentLength">Body length, or -1 when chunked or absent.</param>
/// <param name="Chunked">Whether the body is chunked.</param>
/// <param name="Close">Whether the client closes the connection after this request.</param>
internal sealed record HttpRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query, long ContentLength, bool Chunked, bool Close);

/// <summary>
/// The little HTTP/1.1 LocalSend needs, on a TLS stream: request lines and headers, bodies by length or chunked, and
/// small JSON or empty answers. Not a general server.
/// </summary>
/// <param name="stream">The connection.</param>
internal sealed class HttpConnection(Stream stream)
{
    private const int _maxHeader = 16 * 1024;

    private readonly Stream _stream = stream;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private int _start;
    private int _end;

    /// <summary>
    /// Reads the next request's line and headers; null when the client closed the connection.
    /// </summary>
    public async Task<HttpRequest?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        while (true)
        {
            var line = await ReadLineAsync(cancellationToken);

            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                if (lines.Count == 0)
                {
                    continue;
                }

                break;
            }

            lines.Add(line);

            if (lines.Sum(l => l.Length) > _maxHeader)
            {
                throw new InvalidDataException("Header too large");
            }
        }

        var first = lines[0].Split(' ');

        if (first.Length < 2)
        {
            throw new InvalidDataException("Bad request line");
        }

        var headers = lines.Skip(1).Select(l => l.Split(':', 2)).Where(p => p.Length == 2).GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);
        var target = first[1];
        var question = target.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? target : target[..question];
        var query = question < 0 ? new Dictionary<string, string>() : target[(question + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .GroupBy(p => WebUtility.UrlDecode(p[0]))
            .ToDictionary(g => g.Key, g => WebUtility.UrlDecode(g.Last().Length > 1 ? g.Last()[1] : string.Empty));
        var length = headers.TryGetValue("Content-Length", out var text) && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
        var chunked = headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        var close = headers.TryGetValue("Connection", out var connection) && connection.Equals("close", StringComparison.OrdinalIgnoreCase);
        return new HttpRequest(first[0].ToUpperInvariant(), path, query, length, chunked, close);
    }

    /// <summary>
    /// Reads a small body whole.
    /// </summary>
    public async Task<byte[]> ReadBodyAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await CopyBodyAsync(request, body, cancellationToken, limit);
        return body.ToArray();
    }

    /// <summary>
    /// Reads and drops the body, so the next request can be read.
    /// </summary>
    public Task DrainAsync(HttpRequest request, CancellationToken cancellationToken) => CopyBodyAsync(request, Stream.Null, cancellationToken);

    /// <summary>
    /// Copies the request's body to <paramref name="target"/>.
    /// </summary>
    public async Task CopyBodyAsync(HttpRequest request, Stream target, CancellationToken cancellationToken, long limit = long.MaxValue)
    {
        if (request.Chunked)
        {
            while (true)
            {
                var sizeLine = await ReadLineAsync(cancellationToken) ?? throw new IOException("Connection closed in a chunk");
                var size = long.Parse(sizeLine.Split(';')[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                if (size == 0)
                {
                    // Trailers, then the empty line.
                    while (!string.IsNullOrEmpty(await ReadLineAsync(cancellationToken)))
                    {
                    }

                    return;
                }

                limit -= size;

                if (limit < 0)
                {
                    throw new InvalidDataException("Body too large");
                }

                await CopyExactAsync(size, target, cancellationToken);
                await ReadLineAsync(cancellationToken);
            }
        }

        if (request.ContentLength > limit)
        {
            throw new InvalidDataException("Body too large");
        }

        if (request.ContentLength > 0)
        {
            await CopyExactAsync(request.ContentLength, target, cancellationToken);
        }
    }

    /// <summary>
    /// Answers with a status and no body.
    /// </summary>
    public Task WriteStatusAsync(int status, CancellationToken cancellationToken) => WriteAsync(status, [], null, cancellationToken);

    /// <summary>
    /// Answers with a JSON body.
    /// </summary>
    public Task WriteJsonAsync<T>(int status, T value, CancellationToken cancellationToken) => WriteAsync(status, JsonSerializer.SerializeToUtf8Bytes(value, LocalSendJson.Options), "application/json", cancellationToken);

    private async Task WriteAsync(int status, byte[] body, string? contentType, CancellationToken cancellationToken)
    {
        var header = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {Reason(status)}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n");

        if (contentType is not null)
        {
            header.Append(CultureInfo.InvariantCulture, $"Content-Type: {contentType}\r\n");
        }

        header.Append("\r\n");
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()), cancellationToken);
        await _stream.WriteAsync(body, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        _ => "Error",
    };

    private async Task CopyExactAsync(long count, Stream target, CancellationToken cancellationToken)
    {
        while (count > 0)
        {
            if (_start == _end && !await FillAsync(cancellationToken))
            {
                throw new IOException("Connection closed in the body");
            }

            var take = (int)Math.Min(count, _end - _start);
            await target.WriteAsync(_buffer.AsMemory(_start, take), cancellationToken);
            _start += take;
            count -= take;
        }
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();

        while (true)
        {
            if (_start == _end && !await FillAsync(cancellationToken))
            {
                return line.Length == 0 ? null : line.ToString();
            }

            var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            var stop = newline < 0 ? _end : newline;
            line.Append(Encoding.UTF8.GetString(_buffer, _start, stop - _start));
            _start = newline < 0 ? _end : newline + 1;

            if (line.Length > _maxHeader)
            {
                throw new InvalidDataException("Line too long");
            }

            if (newline >= 0)
            {
                return line.ToString().TrimEnd('\r');
            }
        }
    }

    private async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        _start = 0;
        _end = await _stream.ReadAsync(_buffer, cancellationToken);
        return _end > 0;
    }
}
