using System.Security.Cryptography;
using Fenpo.Core.Channel;

namespace Fenpo.Core.Tests.Channel;

public sealed class BleChannelTests
{
    private static readonly byte[] _key = Convert.FromHexString("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");

    [Fact]
    public void SealedMessageHasTheAgreedLayout()
    {
        var sealedMessage = BleSeal.Seal(_key, BleSender.Pc, 0x0102030405060708, "merhaba"u8);

        Assert.Equal(12 + 7 + 16, sealedMessage.Length);
        Assert.Equal(Convert.FromHexString("020000000102030405060708"), sealedMessage[..12]);
    }

    [Fact]
    public void SealedMessageOpensWithTheSameKeyAndSender()
    {
        var sealedMessage = BleSeal.Seal(_key, BleSender.Phone, 42, "selam"u8);

        Assert.True(BleSeal.TryOpen(_key, sealedMessage, BleSender.Phone, out var plaintext, out var counter));
        Assert.Equal("selam"u8.ToArray(), plaintext);
        Assert.Equal(42ul, counter);
    }

    [Fact]
    public void MatchesAPlainAesGcmImplementation()
    {
        // The iPhone uses CryptoKit's AES.GCM "combined" form; the same nonce, AAD and key must give the same bytes.
        var nonce = Convert.FromHexString("010000000000000000000007");
        var expected = new byte[5];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, 16))
        {
            aes.Encrypt(nonce, "hello"u8, expected, tag, "fenpo-ble-v1"u8);
        }

        var sealedMessage = BleSeal.Seal(_key, BleSender.Phone, 7, "hello"u8);

        Assert.Equal([.. nonce, .. expected, .. tag], sealedMessage);
    }

    [Fact]
    public void TamperedWrongSenderOrWrongKeyDoNotOpen()
    {
        var sealedMessage = BleSeal.Seal(_key, BleSender.Phone, 1, "selam"u8);
        var tampered = (byte[])sealedMessage.Clone();
        tampered[14] ^= 0x01;
        var otherKey = (byte[])_key.Clone();
        otherKey[0] ^= 0xFF;

        Assert.False(BleSeal.TryOpen(_key, tampered, BleSender.Phone, out _, out _));
        Assert.False(BleSeal.TryOpen(_key, sealedMessage, BleSender.Pc, out _, out _));
        Assert.False(BleSeal.TryOpen(otherKey, sealedMessage, BleSender.Phone, out _, out _));
    }

    [Fact]
    public void TooLongMessagesAreRefused() => Assert.Throws<ArgumentException>(() => BleSeal.Seal(_key, BleSender.Pc, 1, new byte[BleSeal.MaxPlaintextLength + 1]));

    [Theory]
    [InlineData(1, 527)]
    [InlineData(600, 527)]
    [InlineData(4096, 23)]
    public void ChunksJoinBackToTheMessage(int length, int attMtu)
    {
        var message = RandomNumberGenerator.GetBytes(length);
        var chunks = BleChunks.Split(message, BleChunks.PayloadLength(attMtu));
        var assembler = new BleChunkAssembler();
        byte[]? joined = null;

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= attMtu - 3);
            assembler.TryAdd(chunk, out joined);
        }

        Assert.Equal(message, joined);
        Assert.Equal(BleChunks.First, (byte)(chunks[0][0] & BleChunks.First));
        Assert.Equal(BleChunks.Last, (byte)(chunks[^1][0] & BleChunks.Last));
        Assert.Equal(0, (chunks[0][1] << 8) | chunks[0][2]);
    }

    [Fact]
    public void ChunkOutOfSequenceDropsTheMessage()
    {
        var chunks = BleChunks.Split(new byte[100], 30);
        var assembler = new BleChunkAssembler();

        assembler.TryAdd(chunks[0], out _);
        Assert.False(assembler.TryAdd(chunks[2], out _));
        Assert.False(assembler.TryAdd(chunks[3], out var dropped));
        Assert.Null(dropped);

        // A new message starts cleanly after the broken one.
        var again = BleChunks.Split("tamam"u8, 30);
        Assert.True(assembler.TryAdd(again[0], out var message));
        Assert.Equal("tamam"u8.ToArray(), message);
    }
}
