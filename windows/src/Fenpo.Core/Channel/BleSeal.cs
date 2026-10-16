using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Fenpo.Core.Channel;

/// <summary>
/// Who sealed a Fenpo BLE message; the first nonce byte (protocol/README.md, "Sealing").
/// </summary>
public enum BleSender : byte
{
    /// <summary>The phone.</summary>
    Phone = 0x01,

    /// <summary>The PC.</summary>
    Pc = 0x02,
}

/// <summary>
/// AES-256-GCM sealing of Fenpo BLE messages: <c>nonce (12) || ciphertext || tag (16)</c>, nonce
/// <c>[sender][0 0 0][8 byte counter, big-endian]</c>, additional data "fenpo-ble-v1".
/// </summary>
public static class BleSeal
{
    /// <summary>Nonce length.</summary>
    public const int NonceLength = 12;

    /// <summary>Tag length.</summary>
    public const int TagLength = 16;

    /// <summary>Key length (AES-256).</summary>
    public const int KeyLength = 32;

    /// <summary>Largest sealed message the channel carries.</summary>
    public const int MaxSealedLength = 4096;

    private static readonly byte[] _associatedData = "fenpo-ble-v1"u8.ToArray();

    /// <summary>
    /// Gets the largest plaintext that still fits in one sealed message.
    /// </summary>
    public static int MaxPlaintextLength => MaxSealedLength - NonceLength - TagLength;

    /// <summary>
    /// Seals a message.
    /// </summary>
    /// <param name="key">Channel key (32 bytes).</param>
    /// <param name="sender">Who seals it.</param>
    /// <param name="counter">The sender's next counter value; never reuse one with the same key.</param>
    /// <param name="plaintext">Message (Envelope bytes).</param>
    /// <returns>The sealed message.</returns>
    /// <exception cref="ArgumentException">The key has the wrong length or the message is too long.</exception>
    public static byte[] Seal(ReadOnlySpan<byte> key, BleSender sender, ulong counter, ReadOnlySpan<byte> plaintext)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException($"The channel key must be {KeyLength} bytes", nameof(key));
        }

        if (plaintext.Length > MaxPlaintextLength)
        {
            throw new ArgumentException($"A sealed BLE message holds at most {MaxPlaintextLength} bytes; larger ones go over Wi-Fi", nameof(plaintext));
        }

        var output = new byte[NonceLength + plaintext.Length + TagLength];
        var nonce = output.AsSpan(0, NonceLength);
        nonce[0] = (byte)sender;
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], counter);

        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceLength, plaintext.Length), output.AsSpan(NonceLength + plaintext.Length), _associatedData);
        return output;
    }

    /// <summary>
    /// Opens a sealed message.
    /// </summary>
    /// <param name="key">Channel key (32 bytes).</param>
    /// <param name="sealedMessage">Sealed message.</param>
    /// <param name="sender">Expected sender.</param>
    /// <param name="plaintext">The message when it opened.</param>
    /// <param name="counter">The sender's counter from the nonce.</param>
    /// <returns>True when the message is authentic and from <paramref name="sender"/>; replay checks are the caller's.</returns>
    public static bool TryOpen(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedMessage, BleSender sender, out byte[] plaintext, out ulong counter)
    {
        plaintext = [];
        counter = 0;

        if (key.Length != KeyLength || sealedMessage.Length < NonceLength + TagLength || sealedMessage.Length > MaxSealedLength)
        {
            return false;
        }

        var nonce = sealedMessage[..NonceLength];

        if (nonce[0] != (byte)sender || nonce[1] != 0 || nonce[2] != 0 || nonce[3] != 0)
        {
            return false;
        }

        counter = BinaryPrimitives.ReadUInt64BigEndian(nonce[4..]);
        var cipherLength = sealedMessage.Length - NonceLength - TagLength;
        var output = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, sealedMessage.Slice(NonceLength, cipherLength), sealedMessage[^TagLength..], output, _associatedData);
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }

        plaintext = output;
        return true;
    }
}
