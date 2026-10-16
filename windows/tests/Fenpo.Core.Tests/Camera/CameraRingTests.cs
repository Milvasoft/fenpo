using System.Runtime.InteropServices;
using Fenpo.Core.Camera;

namespace Fenpo.Core.Tests.Camera;

public sealed unsafe class CameraRingTests : IDisposable
{
    private readonly byte* _memory = (byte*)NativeMemory.AllocZeroed((nuint)CameraRing.Size);
    private readonly byte[] _buffer = new byte[CameraRing.MaxFrame];

    public CameraRingTests() => CameraRing.Initialize(_memory);

    public void Dispose() => NativeMemory.Free(_memory);

    [Fact]
    public void AReaderStartsAtAKeyFrameAndFollowsInOrder()
    {
        CameraRing.Write(_memory, [9], 0, keyFrame: false);
        CameraRing.Write(_memory, [1, 1], 1000, keyFrame: true);
        CameraRing.Write(_memory, [2], 2000, keyFrame: false);

        Assert.True(CameraRing.TryRead(_memory, 0, _buffer, out var first));
        Assert.True(first.KeyFrame);
        Assert.Equal(1000, first.PtsMicroseconds);
        Assert.Equal(2, first.Length);
        Assert.True(CameraRing.TryRead(_memory, first.Sequence, _buffer, out var second));
        Assert.Equal(2, _buffer[0]);
        Assert.False(CameraRing.TryRead(_memory, second.Sequence, _buffer, out _));
    }

    [Fact]
    public void WithoutAKeyFrameThereIsNothingToStartFrom()
    {
        CameraRing.Write(_memory, [1], 0, keyFrame: false);

        Assert.False(CameraRing.TryRead(_memory, 0, _buffer, out _));
    }

    [Fact]
    public void AReaderFarBehindJumpsToTheNewestKeyFrame()
    {
        CameraRing.Write(_memory, [0], 0, keyFrame: true);
        Assert.True(CameraRing.TryRead(_memory, 0, _buffer, out var start));

        for (var i = 1; i <= 40; i++)
        {
            CameraRing.Write(_memory, [(byte)i], i, keyFrame: i == 35);
        }

        Assert.True(CameraRing.TryRead(_memory, start.Sequence, _buffer, out var jumped));
        Assert.True(jumped.KeyFrame);
        Assert.Equal(35, _buffer[0]);
    }

    [Fact]
    public void AFrameTooBigForASlotIsRefused() => Assert.False(CameraRing.Write(_memory, new byte[CameraRing.MaxFrame + 1], 0, keyFrame: true));

    [Fact]
    public void TheReaderFlagNeedsAFreshHeartbeat()
    {
        Assert.False(CameraRing.ReaderActive(_memory, TimeSpan.FromSeconds(3)));
        CameraRing.SetReader(_memory, active: true);
        Assert.True(CameraRing.ReaderActive(_memory, TimeSpan.FromSeconds(3)));
        Assert.False(CameraRing.ReaderActive(_memory, TimeSpan.Zero));
    }
}
