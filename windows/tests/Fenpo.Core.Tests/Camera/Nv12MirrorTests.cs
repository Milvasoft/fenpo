using Fenpo.Core.Camera;

namespace Fenpo.Core.Tests.Camera;

public sealed class Nv12MirrorTests
{
    [Fact]
    public void LumaIsReversedByteByByteAndChromaByPair()
    {
        // 4 × 2 picture: two luma lines, one chroma line of two U,V pairs.
        byte[] source =
        [
            1, 2, 3, 4,
            5, 6, 7, 8,
            10, 20, 11, 21,
        ];
        var target = new byte[source.Length];

        Nv12Mirror.Copy(source, target, 4, 2);

        Assert.Equal(
            [
                4, 3, 2, 1,
                8, 7, 6, 5,
                11, 21, 10, 20,
            ],
            target);
    }

    [Fact]
    public void MirroringTwiceGivesThePictureBack()
    {
        const int width = 64;
        const int height = 36;
        var source = new byte[Nv12Mirror.FrameLength(width, height)];
        new Random(7).NextBytes(source);
        var once = new byte[source.Length];
        var twice = new byte[source.Length];

        Nv12Mirror.Copy(source, once, width, height);
        Nv12Mirror.Copy(once, twice, width, height);

        Assert.NotEqual(source, once);
        Assert.Equal(source, twice);
    }

    [Fact]
    public void BytesAfterThePictureAreLeftAlone()
    {
        var source = new byte[Nv12Mirror.FrameLength(2, 2)];
        var target = new byte[source.Length + 2];
        target[^1] = 99;
        target[^2] = 98;

        Nv12Mirror.Copy(source, target, 2, 2);

        Assert.Equal(98, target[^2]);
        Assert.Equal(99, target[^1]);
    }

    [Fact]
    public void AShortBufferOrOddSizeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Nv12Mirror.Copy(new byte[5], new byte[6], 2, 2));
        Assert.Throws<ArgumentException>(() => Nv12Mirror.Copy(new byte[6], new byte[5], 2, 2));
        Assert.Throws<ArgumentException>(() => Nv12Mirror.Copy(new byte[9], new byte[9], 3, 2));
    }
}
