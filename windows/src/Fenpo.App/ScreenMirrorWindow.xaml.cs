using Fenpo.App.Helpers;
using Fenpo.App.Services;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace Fenpo.App;

/// <summary>
/// The Android phone's screen in a window: the screen stream plays in a <see cref="LiveH264Player"/>; clicks become
/// taps, holds long presses, drags swipes and the wheel a short swipe, all as points 0..1 on the streamed frame.
/// </summary>
public sealed partial class ScreenMirrorWindow : Window
{
    private static readonly TimeSpan _longPress = TimeSpan.FromMilliseconds(500);
    private const double _tapSlop = 8;

    private readonly ScreenMirrorService _mirror;
    private readonly LocalizationService _localization;
    private readonly ILogger _logger;
    private readonly DispatcherQueue _dispatcher;
    private LiveH264Player? _player;
    private LivePcmPlayer? _sound;
    private MediaFeed? _stream;
    private MediaFeed? _soundStream;
    private Point _pressedAt;
    private DateTimeOffset _pressedTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScreenMirrorWindow"/> class.
    /// </summary>
    /// <param name="mirror">Screen mirroring.</param>
    /// <param name="themeService">Applies the Theme setting to this window.</param>
    /// <param name="localization">Strings.</param>
    /// <param name="logger">Logger.</param>
    public ScreenMirrorWindow(ScreenMirrorService mirror, ThemeService themeService, LocalizationService localization, ILogger<ScreenMirrorWindow> logger)
    {
        _mirror = mirror;
        _localization = localization;
        _logger = logger;
        InitializeComponent();
        _dispatcher = DispatcherQueue;

        Title = localization.GetString("Mirror_Title");
        TitleText.Text = Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/Fenpo.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Scale(440), Scale(900)));
        themeService.Register(this);

        _mirror.Changed += OnMirrorChanged;
        _mirror.StreamOpened += OnStreamOpened;
        Closed += (sender, e) => OnClosed();

        // Clicking into the iPhone's picture hands it the keyboard and mouse, as on a Mac.
        Activated += (sender, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.PointerActivated && _mirror.FromIphone && _mirror.State == ScreenState.Streaming)
            {
                App.GetService<PhoneControlService>().Set(true);
            }
        };
        ApplyState();

        // An iPhone broadcast started on the phone opens this window while its stream already runs.
        if (_mirror.State == ScreenState.Streaming && _mirror.Feed is { } feed)
        {
            Play(feed);
        }
    }

    /// <summary>
    /// Shows the window and asks the phone to start.
    /// </summary>
    public void Start()
    {
        Activate();
        _ = _mirror.StartMirroringAsync();
    }

    /// <summary>
    /// Shows the window for a stream the phone started by itself.
    /// </summary>
    public void Show() => Activate();

    private static Envelope Global(ScreenGlobalAction action) => new() { ScreenInput = new ScreenInput { Global = action } };

    private int Scale(int pixels) => (int)(pixels * (Content?.XamlRoot?.RasterizationScale ?? NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0));

    private void OnClosed()
    {
        _mirror.Changed -= OnMirrorChanged;
        _mirror.StreamOpened -= OnStreamOpened;
        _player?.Dispose();
        _player = null;
        _sound?.Dispose();
        _sound = null;
        _ = _mirror.StopMirroringAsync();
    }

    private void OnMirrorChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        ApplyState();

        // After a rotation the same connection carries a new size (new SPS/PPS and a key frame): the player starts
        // over at that key frame instead of relying on the decoder to change size on the fly.
        if (_stream is { } stream && _mirror.Video is { Width: > 0 } video && (video.Width, video.Height) != _player?.Size)
        {
            _logger.LogInformation("Screen size changed to {Width}x{Height}; restarting the player", video.Width, video.Height);
            Play(stream);
            _ = _mirror.RequestKeyFrameAsync();
        }
    });

    private void ApplyState()
    {
        var state = _mirror.State;
        StatePanel.Visibility = state == ScreenState.Streaming ? Visibility.Collapsed : Visibility.Visible;
        Waiting.Visibility = state == ScreenState.WaitingForConsent ? Visibility.Visible : Visibility.Collapsed;
        RestartButton.Visibility = state is ScreenState.PausedLocked or ScreenState.Stopped or ScreenState.Failed ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = _localization.GetString(state switch
        {
            ScreenState.WaitingForConsent when _mirror.FromIphone => "Mirror_WaitingIphone",
            ScreenState.WaitingForConsent => "Mirror_WaitingConsent",
            ScreenState.PausedLocked => "Mirror_Locked",
            ScreenState.Failed => "Mirror_Failed",
            _ => "Mirror_Stopped",
        });
        NoControl.IsOpen = state == ScreenState.Streaming && !_mirror.ControlAvailable && !_mirror.FromIphone;

        // The iPhone is not controlled through the stream: Stop, and handing it the PC's keyboard and mouse.
        foreach (var control in Controls.Children.OfType<FrameworkElement>().Where(c => c != StopButton && c != ControlButton))
        {
            control.Visibility = _mirror.FromIphone ? Visibility.Collapsed : Visibility.Visible;
        }

        ControlButton.Visibility = _mirror.FromIphone ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnControlClick(object sender, RoutedEventArgs e) => App.GetService<PhoneControlService>().Set(true);

    private void OnStreamOpened(object? sender, MediaFeed stream) => _dispatcher.TryEnqueue(() => Play(stream));

    private void Play(MediaFeed stream)
    {
        var video = _stream == stream && _mirror.Video is { Width: > 0 } current ? current : stream.Open.Video;
        _stream = stream;
        _player?.Dispose();
        _player = new LiveH264Player(stream.Packets, video?.Width ?? 0, video?.Height ?? 0, _logger, () => _ = _mirror.RequestKeyFrameAsync());
        Player.Show(_player);

        // A resize restarts only the picture; the sound of the same stream keeps playing.
        if (_sound is null || !ReferenceEquals(_soundStream, stream))
        {
            _sound?.Dispose();
            _sound = stream.Open.Audio == AudioCodec.PcmS16Le ? new LivePcmPlayer(stream.Audio, stream.Open.AudioSampleRate, stream.Open.AudioChannels, _logger) : null;
            _soundStream = stream;
        }
    }

    // Where the frame is inside the element (letterboxed, Uniform) and a point on it as 0..1.
    private ScreenPoint? ToFrame(Point point)
    {
        var session = _player?.Player.PlaybackSession;
        double frameWidth = session?.NaturalVideoWidth ?? 0;
        double frameHeight = session?.NaturalVideoHeight ?? 0;

        if (frameWidth == 0 || frameHeight == 0)
        {
            frameWidth = _mirror.Video?.Width ?? 0;
            frameHeight = _mirror.Video?.Height ?? 0;
        }

        var width = TouchSurface.ActualWidth;
        var height = TouchSurface.ActualHeight;

        if (frameWidth == 0 || frameHeight == 0 || width == 0 || height == 0)
        {
            return null;
        }

        var scale = Math.Min(width / frameWidth, height / frameHeight);
        var shownWidth = frameWidth * scale;
        var shownHeight = frameHeight * scale;
        var x = (point.X - ((width - shownWidth) / 2)) / shownWidth;
        var y = (point.Y - ((height - shownHeight) / 2)) / shownHeight;
        return x is < 0 or > 1 || y is < 0 or > 1 ? null : new ScreenPoint { X = (float)x, Y = (float)y };
    }

    private void Send(ScreenInput input)
    {
        if (_mirror.State == ScreenState.Streaming && _mirror.ControlAvailable)
        {
            _ = _mirror.SendInputAsync(input);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressedAt = e.GetCurrentPoint(TouchSurface).Position;
        _pressedTime = DateTimeOffset.Now;
        TouchSurface.CapturePointer(e.Pointer);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        TouchSurface.ReleasePointerCapture(e.Pointer);
        var releasedAt = e.GetCurrentPoint(TouchSurface).Position;
        var held = DateTimeOffset.Now - _pressedTime;
        var moved = Math.Sqrt(Math.Pow(releasedAt.X - _pressedAt.X, 2) + Math.Pow(releasedAt.Y - _pressedAt.Y, 2));

        if (ToFrame(_pressedAt) is not { } from)
        {
            return;
        }

        if (moved < _tapSlop)
        {
            Send(held >= _longPress ? new ScreenInput { LongPress = from } : new ScreenInput { Tap = from });
            return;
        }

        if (ToFrame(releasedAt) is { } to)
        {
            Send(new ScreenInput { Swipe = new ScreenSwipe { From = from, To = to, DurationMs = (uint)Math.Clamp(held.TotalMilliseconds, 100, 2000) } });
        }
    }

    // One wheel notch scrolls about a fifth of the screen, like a short swipe the other way.
    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TouchSurface);

        if (ToFrame(point.Position) is not { } at)
        {
            return;
        }

        var notches = point.Properties.MouseWheelDelta / 120.0;
        var to = new ScreenPoint { X = at.X, Y = (float)Math.Clamp(at.Y + (notches * 0.2), 0, 1) };
        Send(new ScreenInput { Swipe = new ScreenSwipe { From = at, To = to, DurationMs = 150 } });
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => Send(Global(ScreenGlobalAction.Back).ScreenInput);

    private void OnHomeClick(object sender, RoutedEventArgs e) => Send(Global(ScreenGlobalAction.Home).ScreenInput);

    private void OnRecentsClick(object sender, RoutedEventArgs e) => Send(Global(ScreenGlobalAction.Recents).ScreenInput);

    private void OnNotificationsClick(object sender, RoutedEventArgs e) => Send(Global(ScreenGlobalAction.Notifications).ScreenInput);

    private void OnTypeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || TypeBox.Text.Length == 0)
        {
            return;
        }

        Send(new ScreenInput { Text = TypeBox.Text });
        TypeBox.Text = string.Empty;
        e.Handled = true;
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => Close();

    private void OnRestartClick(object sender, RoutedEventArgs e) => _ = _mirror.StartMirroringAsync();
}
