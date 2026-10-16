using System.Security.Cryptography;

namespace Fenpo.Core.Pairing;

/// <summary>
/// The one-time token in the QR code: 16 random bytes, valid for five minutes and for one pairing. A new token is made
/// after it is used or expires, and the QR code is redrawn.
/// </summary>
public sealed class PairingTokens(TimeProvider? time = null)
{
    /// <summary>
    /// How long a token is valid.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private byte[] _token = [];
    private DateTimeOffset _expires;

    /// <summary>
    /// Raised after a new token was made.
    /// </summary>
    public event EventHandler? Rotated;

    /// <summary>
    /// Gets the current token, making a new one when the current one expired.
    /// </summary>
    public byte[] Current
    {
        get
        {
            lock (_lock)
            {
                if (_token.Length == 0 || _time.GetUtcNow() >= _expires)
                {
                    RotateUnlocked();
                }

                return [.. _token];
            }
        }
    }

    /// <summary>
    /// Gets when the current token expires.
    /// </summary>
    public DateTimeOffset Expires
    {
        get
        {
            lock (_lock)
            {
                return _expires;
            }
        }
    }

    /// <summary>
    /// Uses up the token if it matches and is still valid; a new one is made either way it matched.
    /// </summary>
    /// <param name="token">Token from the phone's PairingRequest.</param>
    /// <returns>True for the valid current token.</returns>
    public bool TryConsume(ReadOnlySpan<byte> token)
    {
        bool valid;

        lock (_lock)
        {
            valid = _token.Length > 0 && _time.GetUtcNow() < _expires && CryptographicOperations.FixedTimeEquals(_token, token);

            if (valid)
            {
                RotateUnlocked();
            }
        }

        return valid;
    }

    /// <summary>
    /// Makes a new token now (for example when the pairing screen opens).
    /// </summary>
    public void Rotate()
    {
        lock (_lock)
        {
            RotateUnlocked();
        }
    }

    private void RotateUnlocked()
    {
        _token = RandomNumberGenerator.GetBytes(16);
        _expires = _time.GetUtcNow() + Lifetime;
        Rotated?.Invoke(this, EventArgs.Empty);
    }
}
