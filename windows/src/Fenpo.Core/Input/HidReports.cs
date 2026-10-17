namespace Fenpo.Core.Input;

/// <summary>
/// The PC as a Bluetooth keyboard and mouse (HID over GATT) for the phone: the report descriptor, keys by their
/// physical position (scan code, so the phone's own keyboard layout decides the letters) and the reports sent.
/// Keyboard reports have id 1 (modifiers, reserved, six keys), mouse reports id 2 (buttons, x, y, wheel).
/// </summary>
public static class HidReports
{
    /// <summary>Report id of the keyboard.</summary>
    public const byte KeyboardId = 1;

    /// <summary>Report id of the mouse.</summary>
    public const byte MouseId = 2;

    /// <summary>
    /// The HID report descriptor: a boot-style keyboard with LEDs (id 1) and a three-button mouse with a wheel (id 2).
    /// </summary>
    public static ReadOnlySpan<byte> ReportMap => [
        0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x85, KeyboardId,
        0x05, 0x07, 0x19, 0xE0, 0x29, 0xE7, 0x15, 0x00, 0x25, 0x01, 0x75, 0x01, 0x95, 0x08, 0x81, 0x02,
        0x95, 0x01, 0x75, 0x08, 0x81, 0x01,
        0x95, 0x05, 0x75, 0x01, 0x05, 0x08, 0x19, 0x01, 0x29, 0x05, 0x91, 0x02,
        0x95, 0x01, 0x75, 0x03, 0x91, 0x01,
        0x95, 0x06, 0x75, 0x08, 0x15, 0x00, 0x25, 0x65, 0x05, 0x07, 0x19, 0x00, 0x29, 0x65, 0x81, 0x00,
        0xC0,
        0x05, 0x01, 0x09, 0x02, 0xA1, 0x01, 0x85, MouseId, 0x09, 0x01, 0xA1, 0x00,
        0x05, 0x09, 0x19, 0x01, 0x29, 0x03, 0x15, 0x00, 0x25, 0x01, 0x95, 0x03, 0x75, 0x01, 0x81, 0x02,
        0x95, 0x01, 0x75, 0x05, 0x81, 0x01,
        0x05, 0x01, 0x09, 0x30, 0x09, 0x31, 0x09, 0x38, 0x15, 0x81, 0x25, 0x7F, 0x75, 0x08, 0x95, 0x03, 0x81, 0x06,
        0xC0, 0xC0,
    ];

    /// <summary>
    /// The HID usage of a key from its set-1 scan code; 0 for keys without one.
    /// </summary>
    /// <param name="scanCode">Scan code.</param>
    /// <param name="extended">Whether it came with the E0 prefix.</param>
    /// <returns>The usage (keyboard page).</returns>
    public static byte Usage(int scanCode, bool extended)
    {
        if (extended)
        {
            return scanCode switch
            {
                0x1C => 0x58, // keypad Enter
                0x35 => 0x54, // keypad /
                0x37 => 0x46, // Print Screen
                0x47 => 0x4A, // Home
                0x48 => 0x52, // Up
                0x49 => 0x4B, // Page Up
                0x4B => 0x50, // Left
                0x4D => 0x4F, // Right
                0x4F => 0x4D, // End
                0x50 => 0x51, // Down
                0x51 => 0x4E, // Page Down
                0x52 => 0x49, // Insert
                0x53 => 0x4C, // Delete
                0x5D => 0x65, // Menu
                _ => 0,
            };
        }

        return scanCode switch
        {
            0x01 => 0x29,
            >= 0x02 and <= 0x0B => (byte)(0x1E + scanCode - 0x02),
            0x0C => 0x2D,
            0x0D => 0x2E,
            0x0E => 0x2A,
            0x0F => 0x2B,
            0x10 => 0x14, 0x11 => 0x1A, 0x12 => 0x08, 0x13 => 0x15, 0x14 => 0x17, 0x15 => 0x1C, 0x16 => 0x18, 0x17 => 0x0C, 0x18 => 0x12, 0x19 => 0x13,
            0x1A => 0x2F,
            0x1B => 0x30,
            0x1C => 0x28,
            0x1E => 0x04, 0x1F => 0x16, 0x20 => 0x07, 0x21 => 0x09, 0x22 => 0x0A, 0x23 => 0x0B, 0x24 => 0x0D, 0x25 => 0x0E, 0x26 => 0x0F,
            0x27 => 0x33,
            0x28 => 0x34,
            0x29 => 0x35,
            0x2B => 0x31,
            0x2C => 0x1D, 0x2D => 0x1B, 0x2E => 0x06, 0x2F => 0x19, 0x30 => 0x05, 0x31 => 0x11, 0x32 => 0x10,
            0x33 => 0x36,
            0x34 => 0x37,
            0x35 => 0x38,
            0x37 => 0x55,
            0x39 => 0x2C,
            0x3A => 0x39,
            >= 0x3B and <= 0x44 => (byte)(0x3A + scanCode - 0x3B),
            0x45 => 0x53,
            0x46 => 0x47,
            0x47 => 0x5F, 0x48 => 0x60, 0x49 => 0x61, 0x4A => 0x56, 0x4B => 0x5C, 0x4C => 0x5D, 0x4D => 0x5E, 0x4E => 0x57,
            0x4F => 0x59, 0x50 => 0x5A, 0x51 => 0x5B, 0x52 => 0x62, 0x53 => 0x63,
            0x56 => 0x64, // the ISO key left of Z (<> on a Turkish keyboard)
            0x57 => 0x44,
            0x58 => 0x45,
            _ => 0,
        };
    }

    /// <summary>
    /// The modifier bit of a key (Ctrl, Shift, Alt, Windows = the phone's Command), or 0.
    /// </summary>
    /// <param name="scanCode">Scan code.</param>
    /// <param name="extended">Whether it came with the E0 prefix.</param>
    /// <returns>The bit in the report's first byte.</returns>
    public static byte Modifier(int scanCode, bool extended) => (scanCode, extended) switch
    {
        (0x1D, false) => 0x01,
        (0x2A, false) => 0x02,
        (0x38, false) => 0x04,
        (0x5B, true) => 0x08,
        (0x1D, true) => 0x10,
        (0x36, false) => 0x20,
        (0x38, true) => 0x40,
        (0x5C, true) => 0x80,
        _ => 0,
    };

    /// <summary>
    /// Mouse reports for a move, a wheel turn and the buttons held; moves beyond what one report holds are split.
    /// </summary>
    /// <param name="dx">Horizontal pixels.</param>
    /// <param name="dy">Vertical pixels.</param>
    /// <param name="wheel">Wheel notches, up positive.</param>
    /// <param name="buttons">Bit 0 left, 1 right, 2 middle.</param>
    /// <returns>Four-byte reports without the id.</returns>
    public static IEnumerable<byte[]> Mouse(int dx, int dy, int wheel, byte buttons)
    {
        do
        {
            var x = (sbyte)Math.Clamp(dx, -127, 127);
            var y = (sbyte)Math.Clamp(dy, -127, 127);
            var w = (sbyte)Math.Clamp(wheel, -127, 127);
            yield return [buttons, (byte)x, (byte)y, (byte)w];
            dx -= x;
            dy -= y;
            wheel -= w;
        }
        while (dx != 0 || dy != 0 || wheel != 0);
    }
}

/// <summary>
/// Which keys are held, as a keyboard report: modifiers and up to six other keys (more are dropped, as on a real
/// keyboard without n-key rollover).
/// </summary>
public sealed class HidKeyboardState
{
    private readonly List<byte> _keys = [];
    private byte _modifiers;

    /// <summary>
    /// A key went down or up.
    /// </summary>
    /// <param name="scanCode">Scan code.</param>
    /// <param name="extended">E0 prefix.</param>
    /// <param name="down">Down or up.</param>
    /// <returns>True when the report changed.</returns>
    public bool Change(int scanCode, bool extended, bool down)
    {
        if (HidReports.Modifier(scanCode, extended) is var bit and not 0)
        {
            var before = _modifiers;
            _modifiers = down ? (byte)(_modifiers | bit) : (byte)(_modifiers & ~bit);
            return before != _modifiers;
        }

        var usage = HidReports.Usage(scanCode, extended);

        if (usage == 0)
        {
            return false;
        }

        if (down)
        {
            if (_keys.Contains(usage) || _keys.Count >= 6)
            {
                return false;
            }

            _keys.Add(usage);
            return true;
        }

        return _keys.Remove(usage);
    }

    /// <summary>
    /// Lets go of every key (leaving control, or the link dropped).
    /// </summary>
    public void Clear()
    {
        _keys.Clear();
        _modifiers = 0;
    }

    /// <summary>
    /// The report without the id: modifiers, reserved, six keys.
    /// </summary>
    /// <returns>Eight bytes.</returns>
    public byte[] Report()
    {
        var report = new byte[8];
        report[0] = _modifiers;

        for (var index = 0; index < _keys.Count; index++)
        {
            report[2 + index] = _keys[index];
        }

        return report;
    }
}
