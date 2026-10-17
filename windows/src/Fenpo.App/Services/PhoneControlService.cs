using System.Runtime.InteropServices;
using System.Threading.Channels;
using Fenpo.Bluetooth.Hid;
using Fenpo.Core.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fenpo.App.Services;

/// <summary>
/// The PC's keyboard and mouse on the phone (HID over GATT): Ctrl+Alt+I, or clicking into the iPhone's mirror window,
/// hands every key and mouse move to the phone until Ctrl+Alt+I again. While it lasts low-level hooks swallow the input
/// on the PC (the pointer stays still) and <see cref="HidPeripheral"/> sends it as keyboard and mouse reports.
/// Ctrl+Alt+Del always reaches Windows.
/// </summary>
public sealed partial class PhoneControlService(HidPeripheral hid, ILogger<PhoneControlService> logger) : IHostedService
{
    private const int _hotkeyId = 0x4650;
    private const uint _wmHotkey = 0x0312;
    private const uint _wmApp = 0x8000;
    private const uint _wmQuit = 0x0012;

    private readonly HidPeripheral _hid = hid;
    private readonly ILogger<PhoneControlService> _logger = logger;
    private readonly HidKeyboardState _keyboard = new();
    private readonly Channel<Action> _input = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private Thread? _thread;
    private uint _threadId;
    private static PhoneControlService? _instance;
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _ctrl;
    private bool _alt;
    private byte _buttons;
    private int _dx;
    private int _dy;
    private int _wheel;

    // The phone takes about one report per Bluetooth connection event (15 ms on the iPhone); sending a report per
    // mouse event (up to 1000 a second) queues them up, and the pointer lags and then jumps. Moves wait until the
    // previous report is out and this much time has passed, summed meanwhile.
    private static readonly TimeSpan MoveInterval = TimeSpan.FromMilliseconds(15);
    private readonly System.Diagnostics.Stopwatch _sinceMove = System.Diagnostics.Stopwatch.StartNew();
    private Task _lastMove = Task.CompletedTask;

    /// <summary>
    /// Raised from any thread when control starts or ends, or the phone's link changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value indicating whether the keyboard and mouse go to the phone.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the phone takes the reports yet (it connects a few seconds after control starts).
    /// </summary>
    public bool PhoneConnected => _hid.Connected;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _instance = this;
        _hid.Changed += (sender, e) => Changed?.Invoke(this, EventArgs.Empty);
        _thread = new Thread(Run) { IsBackground = true, Name = "Fenpo phone control" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ = Task.Run(SendLoopAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_threadId != 0)
        {
            PostThreadMessageW(_threadId, _wmQuit, 0, 0);
        }

        await _hid.StopAsync();
    }

    /// <summary>
    /// Starts or ends control from anywhere (the mirror window's button, the tray).
    /// </summary>
    /// <param name="on">Start or end.</param>
    public void Set(bool on)
    {
        if (_threadId != 0)
        {
            PostThreadMessageW(_threadId, _wmApp, on ? 1 : 0, 0);
        }
    }

    // The hooks and the hotkey live on this thread's message loop.
    private void Run()
    {
        _threadId = GetCurrentThreadId();

        // Ctrl+Alt+I, no repeat.
        if (!RegisterHotKey(0, _hotkeyId, 0x0002 | 0x0001 | 0x4000, 0x49))
        {
            _logger.LogWarning("Ctrl+Alt+I could not be registered: {Error}", Marshal.GetLastPInvokeError());
        }

        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.Kind == _wmHotkey && message.WParam == _hotkeyId)
            {
                Toggle(!Active);
            }
            else if (message.Kind == _wmApp)
            {
                Toggle(message.WParam != 0);
            }
        }

        Toggle(false);
        UnregisterHotKey(0, _hotkeyId);
    }

    private unsafe void Toggle(bool on)
    {
        if (on == Active)
        {
            return;
        }

        Active = on;

        if (on)
        {
            _ = _hid.StartAsync();
            var module = GetModuleHandleW(null);
            _keyboardHook = SetWindowsHookExW(13, &KeyboardHook, module, 0);
            _mouseHook = SetWindowsHookExW(14, &MouseHook, module, 0);
            _logger.LogInformation("Keyboard and mouse go to the phone");
        }
        else
        {
            UnhookWindowsHookEx(_keyboardHook);
            UnhookWindowsHookEx(_mouseHook);
            _keyboardHook = 0;
            _mouseHook = 0;
            _ctrl = false;
            _alt = false;

            // Let go of everything on the phone before the keyboard goes away there.
            _input.Writer.TryWrite(() =>
            {
                _keyboard.Clear();
                _buttons = 0;
                _ = _hid.SendKeyboardAsync(_keyboard.Report());
                _ = _hid.SendMouseAsync([0, 0, 0, 0]);
            });
            _ = StopLaterAsync();
            _logger.LogInformation("Keyboard and mouse are back on the PC");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopLaterAsync()
    {
        await Task.Delay(500);

        if (!Active)
        {
            await _hid.StopAsync();
        }
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardHook(int code, nint wParam, nint lParam) => _instance?.OnKeyboard(code, wParam, lParam) ?? CallNextHookEx(0, code, wParam, lParam);

    [UnmanagedCallersOnly]
    private static nint MouseHook(int code, nint wParam, nint lParam) => _instance?.OnMouse(code, wParam, lParam) ?? CallNextHookEx(0, code, wParam, lParam);

    private unsafe nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var key = (KeyboardHookData*)lParam;

        // Injected input (other tools, Fenpo's own presenter) is left alone.
        if ((key->Flags & 0x10) != 0)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var down = wParam is 0x0100 or 0x0104;
        var extended = (key->Flags & 0x01) != 0;
        var scan = (int)key->ScanCode;

        if (key->VirtualKey is 0x11 or 0xA2 or 0xA3)
        {
            _ctrl = down;
        }
        else if (key->VirtualKey is 0x12 or 0xA4 or 0xA5)
        {
            _alt = down;
        }

        // Ctrl+Alt+I ends control here, since the hook keeps the hotkey from Windows.
        if (down && key->VirtualKey == 0x49 && _ctrl && _alt)
        {
            Toggle(false);
            return 1;
        }

        _input.Writer.TryWrite(() =>
        {
            if (_keyboard.Change(scan, extended, down))
            {
                _ = _hid.SendKeyboardAsync(_keyboard.Report());
            }
        });
        return 1;
    }

    private unsafe nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var mouse = (MouseHookData*)lParam;

        if ((mouse->Flags & 0x01) != 0)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        switch ((uint)wParam)
        {
            case 0x0200:
                // The pointer does not move on the PC: the distance to where it would have gone is the phone's move.
                GetCursorPos(out var at);
                var dx = mouse->X - at.X;
                var dy = mouse->Y - at.Y;
                _input.Writer.TryWrite(() => { _dx += dx; _dy += dy; });
                break;
            case 0x0201: Button(0x01, true); break;
            case 0x0202: Button(0x01, false); break;
            case 0x0204: Button(0x02, true); break;
            case 0x0205: Button(0x02, false); break;
            case 0x0207: Button(0x04, true); break;
            case 0x0208: Button(0x04, false); break;
            case 0x020A:
                var notches = (short)(mouse->MouseData >> 16) / 120;
                _input.Writer.TryWrite(() => _wheel += notches);
                break;
        }

        return 1;
    }

    private void Button(byte bit, bool down) => _input.Writer.TryWrite(() =>
    {
        _buttons = down ? (byte)(_buttons | bit) : (byte)(_buttons & ~bit);
        Flush(force: true);
    });

    // Input is handled off the hook thread (hooks must return at once); moves are summed and sent at most every
    // MoveInterval.
    private async Task SendLoopAsync()
    {
        var reader = _input.Reader;

        while (true)
        {
            var wait = reader.WaitToReadAsync().AsTask();
            var timer = Task.Delay(8);
            await Task.WhenAny(wait, timer);

            while (reader.TryRead(out var action))
            {
                action();
            }

            Flush(force: false);
        }
    }

    private void Flush(bool force)
    {
        if (!force && _dx == 0 && _dy == 0 && _wheel == 0)
        {
            return;
        }

        if (!force && (!_lastMove.IsCompleted || _sinceMove.Elapsed < MoveInterval))
        {
            return;
        }

        foreach (var report in HidReports.Mouse(_dx, _dy, _wheel, _buttons))
        {
            _lastMove = _hid.SendMouseAsync(report);
        }

        _sinceMove.Restart();

        _dx = 0;
        _dy = 0;
        _wheel = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Kind;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point At;
        public uint Private;
    }

    [LibraryImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32", EntryPoint = "GetMessageW")]
    private static partial int GetMessageW(out NativeMessage message, nint window, uint min, uint max);

    [LibraryImport("user32", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint thread, uint message, nint wParam, nint lParam);

    [LibraryImport("user32", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static unsafe partial nint SetWindowsHookExW(int hook, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint thread);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("kernel32")]
    private static partial uint GetCurrentThreadId();
}
