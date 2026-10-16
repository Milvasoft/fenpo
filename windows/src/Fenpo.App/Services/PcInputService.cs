using System.Runtime.InteropServices;
using Fenpo.App.Helpers;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Fenpo.App.Services;

/// <summary>
/// The phone as the PC's touchpad, keyboard and presentation remote (CAPABILITY_PC_INPUT, pc_input.proto): pointer,
/// clicks, wheel, keys and text go into the Windows session, presenter actions become the usual slideshow keys, the
/// laser pointer is a dot over the primary screen, and keep-awake holds the screen on until the phone lets go or
/// leaves.
/// </summary>
public sealed partial class PcInputService(PhoneChannel channel, ILogger<PcInputService> logger) : IHostedService
{
    private const ushort _shift = 0x10;
    private const ushort _control = 0x11;
    private const ushort _alt = 0x12;
    private const ushort _windows = 0x5B;

    // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED, and ES_CONTINUOUS alone to let go.
    private const uint _awake = 0x80000003;
    private const uint _release = 0x80000000;

    private readonly PhoneChannel _channel = channel;
    private readonly ILogger<PcInputService> _logger = logger;
    private readonly Lock _lock = new();
    private DispatcherQueue? _dispatcher;
    private LaserWindow? _laser;
    private float _moveX;
    private float _moveY;
    private float _scrollX;
    private float _scrollY;
    private bool _keepingAwake;

    /// <summary>
    /// Takes the UI thread for the laser window and keep-awake (both belong to a long-lived thread). Call once.
    /// </summary>
    /// <param name="dispatcher">UI thread dispatcher.</param>
    public void AttachUi(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) =>
        {
            if (message.Envelope.BodyCase == Envelope.BodyOneofCase.PcInput)
            {
                Handle(message.Envelope.PcInput);
            }
        };

        // A phone that leaves takes its laser and keep-awake with it.
        _channel.Changed += (sender, e) =>
        {
            if (!_channel.AndroidConnected && !_channel.IsConnected)
            {
                OnUi(() =>
                {
                    _laser?.HideDot();
                    KeepAwake(false);
                });
            }
        };
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ushort KeyOf(PcKey key) => key.Code switch
    {
        PcKeyCode.Enter => 0x0D,
        PcKeyCode.Backspace => 0x08,
        PcKeyCode.Delete => 0x2E,
        PcKeyCode.Tab => 0x09,
        PcKeyCode.Escape => 0x1B,
        PcKeyCode.Space => 0x20,
        PcKeyCode.Left => 0x25,
        PcKeyCode.Right => 0x27,
        PcKeyCode.Up => 0x26,
        PcKeyCode.Down => 0x28,
        PcKeyCode.PageUp => 0x21,
        PcKeyCode.PageDown => 0x22,
        PcKeyCode.Home => 0x24,
        PcKeyCode.End => 0x23,
        PcKeyCode.F5 or PcKeyCode.ShiftF5 => 0x74,
        PcKeyCode.BlackScreen => 0x42,
        PcKeyCode.Windows => _windows,
        _ => (ushort)key.VirtualKey,
    };

    private static void Press(ushort key, IReadOnlyList<ushort> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            InputInjector.Key(modifier, down: true);
        }

        InputInjector.Key(key, down: true);
        InputInjector.Key(key, down: false);

        foreach (var modifier in modifiers.Reverse())
        {
            InputInjector.Key(modifier, down: false);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);

    private void Handle(PcInput input)
    {
        switch (input.ActionCase)
        {
            case PcInput.ActionOneofCase.Move:
                Move(input.Move.Dx, input.Move.Dy);
                break;
            case PcInput.ActionOneofCase.Click:
                Click(input.Click);
                break;
            case PcInput.ActionOneofCase.Scroll:
                Scroll(input.Scroll.Dx, input.Scroll.Dy);
                break;
            case PcInput.ActionOneofCase.Key:
                Key(input.Key);
                break;
            case PcInput.ActionOneofCase.Text:
                InputInjector.Type(input.Text);
                break;
            case PcInput.ActionOneofCase.Presenter:
                Present(input.Presenter);
                break;
            case PcInput.ActionOneofCase.Laser:
                var laser = input.Laser;
                OnUi(() =>
                {
                    if (laser.Active)
                    {
                        (_laser ??= new LaserWindow()).ShowAt(laser.X, laser.Y);
                    }
                    else
                    {
                        _laser?.HideDot();
                    }
                });
                break;
            case PcInput.ActionOneofCase.KeepAwake:
                var awake = input.KeepAwake;
                OnUi(() => KeepAwake(awake));
                break;
        }
    }

    // Fractions of a pixel or a notch add up, so slow touchpad moves are not lost.
    private void Move(float dx, float dy)
    {
        int x;
        int y;

        lock (_lock)
        {
            _moveX += dx;
            _moveY += dy;
            x = (int)_moveX;
            y = (int)_moveY;
            _moveX -= x;
            _moveY -= y;
        }

        if (x != 0 || y != 0)
        {
            InputInjector.Move(x, y);
        }
    }

    private void Scroll(float dx, float dy)
    {
        int x;
        int y;

        lock (_lock)
        {
            _scrollX += dx;
            _scrollY += dy;
            x = (int)_scrollX;
            y = (int)_scrollY;
            _scrollX -= x;
            _scrollY -= y;
        }

        InputInjector.Scroll(y, x);
    }

    private static void Click(PcClick click)
    {
        var button = click.Button switch
        {
            PcButton.Right => InputInjector.Button.Right,
            PcButton.Middle => InputInjector.Button.Middle,
            _ => InputInjector.Button.Left,
        };

        switch (click.Kind)
        {
            case PcClickKind.Press:
                InputInjector.ButtonEvent(button, down: true);
                break;
            case PcClickKind.Release:
                InputInjector.ButtonEvent(button, down: false);
                break;
            case PcClickKind.Double:
                InputInjector.ButtonEvent(button, down: true);
                InputInjector.ButtonEvent(button, down: false);
                InputInjector.ButtonEvent(button, down: true);
                InputInjector.ButtonEvent(button, down: false);
                break;
            default:
                InputInjector.ButtonEvent(button, down: true);
                InputInjector.ButtonEvent(button, down: false);
                break;
        }
    }

    private void Key(PcKey key)
    {
        var code = KeyOf(key);

        if (code == 0)
        {
            return;
        }

        var modifiers = new List<ushort>();

        if (key.Ctrl)
        {
            modifiers.Add(_control);
        }

        if (key.Alt)
        {
            modifiers.Add(_alt);
        }

        if (key.Shift || key.Code == PcKeyCode.ShiftF5)
        {
            modifiers.Add(_shift);
        }

        if (key.Win && code != _windows)
        {
            modifiers.Add(_windows);
        }

        switch (key.Phase)
        {
            case PcKeyPhase.Down:
                modifiers.ForEach(m => InputInjector.Key(m, down: true));
                InputInjector.Key(code, down: true);
                break;
            case PcKeyPhase.Up:
                InputInjector.Key(code, down: false);
                modifiers.AsEnumerable().Reverse().ToList().ForEach(m => InputInjector.Key(m, down: false));
                break;
            default:
                Press(code, modifiers);
                break;
        }
    }

    // The keys PowerPoint, Keynote on the web, Google Slides and PDF viewers all take.
    private void Present(PcPresenterAction action)
    {
        var key = action switch
        {
            PcPresenterAction.Next => (ushort)0x27,
            PcPresenterAction.Previous => (ushort)0x25,
            PcPresenterAction.Start => (ushort)0x74,
            PcPresenterAction.End => (ushort)0x1B,
            PcPresenterAction.Black => (ushort)0x42,
            _ => (ushort)0,
        };

        if (key != 0)
        {
            Press(key, []);
            _logger.LogDebug("Presenter {Action}", action);
        }
    }

    // UI thread: the execution state belongs to the thread that set it.
    private void KeepAwake(bool on)
    {
        if (on == _keepingAwake)
        {
            return;
        }

        _keepingAwake = on;
        SetThreadExecutionState(on ? _awake : _release);
        _logger.LogInformation("Keeping the screen on for the phone: {On}", on);
    }

    private void OnUi(Action action) => _dispatcher?.TryEnqueue(() => action());
}
