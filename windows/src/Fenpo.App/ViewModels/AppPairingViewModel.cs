using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.Core.Channel;
using Fenpo.Core.Devices;
using Fenpo.Core.Pairing;
using Fenpo.Protocol.V1;
using Fenpo.Transport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Storage.Streams;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Pairing with the Fenpo iPhone app (design "iPhone’unu bağla" with the QR code): the phone reads the QR, pairs over
/// Wi‑Fi (④), then bonds over Bluetooth and shares notifications.
/// </summary>
public sealed partial class AppPairingViewModel : ObservableObject, IDisposable
{
    private readonly ControlServer _server;
    private readonly PairingTokens _tokens;
    private readonly IPhoneStatusSource _status;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _refresh;
    private bool _closing;

    // The phone that paired on this screen; until then no step after the scan is done, whatever other phone is connected.
    private PhoneKind? _pairedKind;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppPairingViewModel"/> class.
    /// </summary>
    /// <param name="server">Control server (QR content, pairing events).</param>
    /// <param name="tokens">One-time tokens.</param>
    /// <param name="status">Phone status (Bluetooth and notification steps).</param>
    public AppPairingViewModel(ControlServer server, PairingTokens tokens, IPhoneStatusSource status)
    {
        _server = server;
        _tokens = tokens;
        _status = status;

        // A fresh token whenever the screen opens; redraw when it is used or runs out.
        _refresh = _dispatcher.CreateTimer();
        _refresh.Tick += (sender, e) => DrawQr();
        _tokens.Rotated += OnTokenRotated;
        _server.Paired += OnPaired;
        _status.Changed += OnStatusChanged;
        _tokens.Rotate();
        ApplySteps();
    }

    /// <summary>
    /// Raised when the screen should close.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>Gets or sets the QR code.</summary>
    [ObservableProperty]
    public partial ImageSource? QrImage { get; set; }

    /// <summary>Gets or sets a value indicating whether the PC is on a network phones can reach.</summary>
    [ObservableProperty]
    public partial bool HasNetwork { get; set; } = true;

    /// <summary>Gets or sets the scan step.</summary>
    [ObservableProperty]
    public partial StepState ScanStep { get; set; } = StepState.Current;

    /// <summary>Gets or sets the Bluetooth step.</summary>
    [ObservableProperty]
    public partial StepState BluetoothStep { get; set; } = StepState.Pending;

    /// <summary>Gets or sets the messages step.</summary>
    [ObservableProperty]
    public partial StepState MessagesStep { get; set; } = StepState.Pending;

    /// <summary>
    /// Gets or sets a value indicating whether to warn that the phone's sound may come to this PC: Android turns on
    /// the headset and A2DP connections by itself once the Bluetooth bond exists, and Fenpo cannot change that
    /// (ANDROID.md A2).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowAudioNotice { get; set; }

    /// <summary>Gets or sets a value indicating whether the phone paired over Wi‑Fi.</summary>
    [ObservableProperty]
    public partial bool IsPaired { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _refresh.Stop();
        _tokens.Rotated -= OnTokenRotated;
        _server.Paired -= OnPaired;
        _status.Changed -= OnStatusChanged;
    }

    [RelayCommand]
    private void Later() => Closed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private static async Task OpenBluetoothSettingsAsync() => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));

    private void OnTokenRotated(object? sender, EventArgs e) => _dispatcher.TryEnqueue(DrawQr);

    private void OnPaired(object? sender, (PairedAppDevice Device, DevicePlatform Platform) paired) => _dispatcher.TryEnqueue(() =>
    {
        IsPaired = true;
        ShowAudioNotice = paired.Platform == DevicePlatform.Android;
        _pairedKind = paired.Platform == DevicePlatform.Android ? PhoneKind.Android : PhoneKind.Iphone;
        ApplySteps();
    });

    private void OnStatusChanged(object? sender, PhoneStatus status) => _dispatcher.TryEnqueue(ApplySteps);

    private void ApplySteps()
    {
        if (_pairedKind is not { } kind)
        {
            ScanStep = StepState.Current;
            BluetoothStep = StepState.Pending;
            MessagesStep = StepState.Pending;
            return;
        }

        var status = _status.StatusOf(kind);
        var bluetoothDone = status.AppConnected && status.NotificationsAvailable;
        ScanStep = IsPaired ? StepState.Done : StepState.Current;
        BluetoothStep = bluetoothDone ? StepState.Done : IsPaired ? StepState.Current : StepState.Pending;
        MessagesStep = status.MessagesAvailable ? StepState.Done : bluetoothDone ? StepState.Current : StepState.Pending;

        // Messages are optional; once the app is linked over Bluetooth and notifications flow, the screen is done.
        if (IsPaired && bluetoothDone && !ShowAudioNotice && !_closing)
        {
            _closing = true;
            var close = _dispatcher.CreateTimer();
            close.Interval = TimeSpan.FromSeconds(3);
            close.IsRepeating = false;
            close.Tick += (sender, e) => Closed?.Invoke(this, EventArgs.Empty);
            close.Start();
        }
    }

    private void DrawQr()
    {
        var uri = _server.PairingUri();
        HasNetwork = uri.Addresses.Count > 0;

        // Level Q leaves room for the logo in the middle (design .qr .logo).
        using var data = QRCodeGenerator.GenerateQrCode(uri.ToString(), QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(12, [0x1B, 0x1F, 0x3B, 0xFF], [0xFF, 0xFF, 0xFF, 0xFF], drawQuietZones: false);
        _ = ShowAsync(png);

        _refresh.Stop();
        _refresh.Interval = (_tokens.Expires - DateTimeOffset.UtcNow) + TimeSpan.FromSeconds(1);
        _refresh.IsRepeating = false;
        _refresh.Start();
    }

    private async Task ShowAsync(byte[] png)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        QrImage = image;
    }
}
