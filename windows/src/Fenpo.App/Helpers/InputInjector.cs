using System.Runtime.InteropServices;

namespace Fenpo.App.Helpers;

/// <summary>
/// Puts pointer, wheel and keyboard input into the current Windows session (SendInput). Relative moves get Windows'
/// own pointer acceleration; text goes as Unicode, whatever the keyboard layout. Windows keeps it out of elevated
/// windows and the secure desktop.
/// </summary>
internal static partial class InputInjector
{
    private const uint _mouse = 0;
    private const uint _keyboard = 1;
    private const uint _move = 0x0001;
    private const uint _wheel = 0x0800;
    private const uint _horizontalWheel = 0x1000;
    private const uint _extendedKey = 0x0001;
    private const uint _keyUp = 0x0002;
    private const uint _unicode = 0x0004;
    private const int _wheelNotch = 120;

    // Keys that need the extended flag to reach the arrow/navigation block rather than the number pad.
    private static readonly HashSet<ushort> _extended = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C];

    /// <summary>
    /// Mouse buttons.
    /// </summary>
    public enum Button
    {
        /// <summary>Left.</summary>
        Left,

        /// <summary>Right.</summary>
        Right,

        /// <summary>Middle.</summary>
        Middle,
    }

    /// <summary>
    /// Moves the pointer by whole pixels.
    /// </summary>
    /// <param name="dx">Right.</param>
    /// <param name="dy">Down.</param>
    public static void Move(int dx, int dy) => Send(Mouse(dx, dy, 0, _move));

    /// <summary>
    /// Presses or releases a button.
    /// </summary>
    /// <param name="button">Button.</param>
    /// <param name="down">True to press.</param>
    public static void ButtonEvent(Button button, bool down)
    {
        var flags = button switch
        {
            Button.Right => down ? 0x0008u : 0x0010u,
            Button.Middle => down ? 0x0020u : 0x0040u,
            _ => down ? 0x0002u : 0x0004u,
        };
        Send(Mouse(0, 0, 0, flags));
    }

    /// <summary>
    /// Turns the wheel by whole notches.
    /// </summary>
    /// <param name="notchesDown">Positive scrolls the content down.</param>
    /// <param name="notchesRight">Positive scrolls the content right.</param>
    public static void Scroll(int notchesDown, int notchesRight)
    {
        if (notchesDown != 0)
        {
            Send(Mouse(0, 0, unchecked((uint)(-notchesDown * _wheelNotch)), _wheel));
        }

        if (notchesRight != 0)
        {
            Send(Mouse(0, 0, unchecked((uint)(notchesRight * _wheelNotch)), _horizontalWheel));
        }
    }

    /// <summary>
    /// Presses or releases a key by its virtual-key code.
    /// </summary>
    /// <param name="virtualKey">Virtual-key code.</param>
    /// <param name="down">True to press.</param>
    public static void Key(ushort virtualKey, bool down) => Send(Keyboard(virtualKey, 0, (down ? 0 : _keyUp) | (_extended.Contains(virtualKey) ? _extendedKey : 0)));

    /// <summary>
    /// Types text as Unicode characters.
    /// </summary>
    /// <param name="text">Text.</param>
    public static void Type(string text)
    {
        var inputs = new List<Input>(text.Length * 2);

        foreach (var c in text)
        {
            inputs.Add(Keyboard(0, c, _unicode));
            inputs.Add(Keyboard(0, c, _unicode | _keyUp));
        }

        Send([.. inputs]);
    }

    private static Input Mouse(int dx, int dy, uint data, uint flags) => new() { Type = _mouse, Union = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags } } };

    private static Input Keyboard(ushort virtualKey, ushort scan, uint flags) => new() { Type = _keyboard, Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Scan = scan, Flags = flags } } };

    private static void Send(params Input[] inputs)
    {
        if (inputs.Length > 0)
        {
            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
