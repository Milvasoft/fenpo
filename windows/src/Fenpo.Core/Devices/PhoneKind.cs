namespace Fenpo.Core.Devices;

/// <summary>
/// Which of the paired phones Fenpo uses. An iPhone comes over Bluetooth (ANCS, AMS, MAP) and, with Fenpo on it, the
/// Fenpo channel; an Android phone comes over the Fenpo channel and its own MAP. One is in use at a time.
/// </summary>
public enum PhoneKind
{
    /// <summary>The iPhone paired in Windows Bluetooth settings.</summary>
    Iphone,

    /// <summary>The Android phone paired with Fenpo on it.</summary>
    Android,
}
