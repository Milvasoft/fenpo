using System.Runtime.InteropServices;

namespace Fenpo.Core.Camera;

/// <summary>
/// Mirrors an NV12 picture (flips it horizontally) for Fenpo Kamera. Every line is reversed: luma byte by byte, chroma
/// by U and V pair, so each pair keeps its order. Allocates nothing; it runs for every frame. The file is linked into
/// Fenpo.VirtualCamera too, so it uses nothing else from Core.
/// </summary>
public static class Nv12Mirror
{
    /// <summary>
    /// Copies an NV12 picture mirrored. The stride is the width, for luma and chroma alike.
    /// </summary>
    /// <param name="source">The picture: <paramref name="width"/> × <paramref name="height"/> luma, then the
    /// interleaved chroma at half the height.</param>
    /// <param name="target">Receives the mirrored picture; at least as long as the picture.</param>
    /// <param name="width">Width in pixels, even.</param>
    /// <param name="height">Height in pixels, even.</param>
    /// <exception cref="ArgumentException">When the size is odd or a buffer is too short.</exception>
    public static void Copy(ReadOnlySpan<byte> source, Span<byte> target, int width, int height)
    {
        var length = FrameLength(width, height);

        if ((width & 1) != 0 || (height & 1) != 0 || source.Length < length || target.Length < length)
        {
            throw new ArgumentException("The NV12 picture's size and buffers do not match.");
        }

        var lumaLength = width * height;

        for (var offset = 0; offset < lumaLength; offset += width)
        {
            var line = target.Slice(offset, width);
            source.Slice(offset, width).CopyTo(line);
            line.Reverse();
        }

        for (var offset = lumaLength; offset < length; offset += width)
        {
            var line = target.Slice(offset, width);
            source.Slice(offset, width).CopyTo(line);

            // Reversing 16-bit elements moves whole U and V pairs and keeps U before V.
            MemoryMarshal.Cast<byte, ushort>(line).Reverse();
        }
    }

    /// <summary>
    /// Bytes of an NV12 picture whose stride is its width.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>Luma plus chroma bytes.</returns>
    public static int FrameLength(int width, int height) => width * height * 3 / 2;
}
