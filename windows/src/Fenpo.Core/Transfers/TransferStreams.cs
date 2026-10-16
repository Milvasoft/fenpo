using System.Security.Cryptography;
using TransferFile = Fenpo.Protocol.V1.FileInfo;

namespace Fenpo.Core.Transfers;

/// <summary>
/// The bytes of a file transfer on its data connection: every file in offer order, exactly its announced size, no
/// separators (protocol/fenpo/v1/transfer.proto).
/// </summary>
public static class TransferStreams
{
    private const int _bufferSize = 128 * 1024;

    /// <summary>
    /// Writes the files in order.
    /// </summary>
    /// <param name="target">Data connection.</param>
    /// <param name="paths">Files, in offer order; each must still have the size announced in the offer.</param>
    /// <param name="progress">Called with the bytes written so far.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every byte is written and flushed.</returns>
    /// <exception cref="InvalidDataException">A file changed size since the offer.</exception>
    public static async Task SendAsync(Stream target, IReadOnlyList<(string Path, ulong Size)> paths, Action<ulong> progress, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[_bufferSize];
        ulong done = 0;

        foreach (var (path, size) in paths)
        {
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, useAsync: true);

            if ((ulong)source.Length != size)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} changed size since the offer");
            }

            var left = size;

            while (left > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min((ulong)buffer.Length, left)), cancellationToken);

                if (read == 0)
                {
                    throw new InvalidDataException($"{Path.GetFileName(path)} ended early");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                left -= (ulong)read;
                done += (ulong)read;
                progress(done);
            }
        }

        await target.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the files into a folder. Each file is written under a temporary name and renamed when complete and its
    /// SHA-256 (if given) matches; on failure nothing partial is left behind.
    /// </summary>
    /// <param name="source">Data connection.</param>
    /// <param name="files">Files from the offer.</param>
    /// <param name="folder">Incoming folder; created when missing.</param>
    /// <param name="progress">Called with the bytes read so far.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paths of the saved files, in offer order.</returns>
    /// <exception cref="EndOfStreamException">The connection closed before every byte arrived.</exception>
    /// <exception cref="InvalidDataException">A file's SHA-256 does not match.</exception>
    public static async Task<IReadOnlyList<string>> ReceiveAsync(Stream source, IReadOnlyList<TransferFile> files, string folder, Action<ulong> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var buffer = new byte[_bufferSize];
        var saved = new List<string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ulong done = 0;

        try
        {
            foreach (var file in files)
            {
                var path = IncomingFileNames.Unique(folder, IncomingFileNames.Safe(file.Name), taken);
                var partial = path + ".fenpo-part";
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                try
                {
                    await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, _bufferSize, useAsync: true))
                    {
                        var left = file.Size;

                        while (left > 0)
                        {
                            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min((ulong)buffer.Length, left)), cancellationToken);

                            if (read == 0)
                            {
                                throw new EndOfStreamException($"The connection closed inside {file.Name}");
                            }

                            hash.AppendData(buffer, 0, read);
                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            left -= (ulong)read;
                            done += (ulong)read;
                            progress(done);
                        }
                    }

                    if (!file.Sha256.IsEmpty && !file.Sha256.Span.SequenceEqual(hash.GetHashAndReset()))
                    {
                        throw new InvalidDataException($"SHA-256 of {file.Name} does not match");
                    }

                    File.Move(partial, path);
                    saved.Add(path);
                }
                finally
                {
                    File.Delete(partial);
                }
            }
        }
        catch
        {
            foreach (var path in saved)
            {
                File.Delete(path);
            }

            throw;
        }

        return saved;
    }
}
