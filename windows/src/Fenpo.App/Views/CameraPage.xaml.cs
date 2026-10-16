using Fenpo.App.Helpers;
using Fenpo.App.Services;
using Fenpo.App.ViewModels;
using Fenpo.Protocol.V1;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Fenpo.App.Views;

/// <summary>
/// Camera page: lens and options, and a live preview of the phone's camera.
/// </summary>
public sealed partial class CameraPage : Page
{
    private readonly CameraService _camera = App.GetService<CameraService>();
    private readonly VirtualCameraService _virtualCamera = App.GetService<VirtualCameraService>();
    private bool _installFailed;
    private readonly LocalizationService _localization = App.GetService<LocalizationService>();
    private readonly ILogger<CameraPage> _logger = App.GetService<ILogger<CameraPage>>();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private LiveH264Player? _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraPage"/> class.
    /// </summary>
    public CameraPage()
    {
        ViewModel = App.GetService<AppFeatureViewModel>();
        InitializeComponent();
        (ViewModel.LensIndex switch
        {
            1 => LensUltraWide,
            2 => LensFront,
            _ => LensWide,
        }).IsChecked = true;

        _camera.Changed += OnCameraChanged;
        _camera.StreamOpened += OnStreamOpened;
        _virtualCamera.Changed += OnVirtualCameraChanged;
        ApplyState();
        ApplyVirtualCamera();
    }

    /// <summary>
    /// Gets the view model.
    /// </summary>
    public AppFeatureViewModel ViewModel { get; }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
        _camera.Changed -= OnCameraChanged;
        _camera.StreamOpened -= OnStreamOpened;
        _virtualCamera.Changed -= OnVirtualCameraChanged;
        _player?.Dispose();
        _player = null;

        // The preview is this page's own; leaving the page ends it, unless an app is using Fenpo Kamera.
        if (!_virtualCamera.InUse)
        {
            _ = _camera.StopCameraAsync();
        }
    }

    private void OnLensChecked(object sender, RoutedEventArgs e) => ViewModel.LensIndex = ReferenceEquals(sender, LensUltraWide) ? 1 : ReferenceEquals(sender, LensFront) ? 2 : 0;

    private void OnStartClick(object sender, RoutedEventArgs e) => _ = _camera.StartCameraAsync("Fenpo");

    private void OnStopClick(object sender, RoutedEventArgs e) => _ = _camera.StopCameraAsync();

    private void OnCameraChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(ApplyState);

    private void OnStreamOpened(object? sender, MediaFeed feed) => _dispatcher.TryEnqueue(() =>
    {
        _player?.Dispose();
        _player = new LiveH264Player(feed.Packets, feed.Open.Video?.Width ?? 0, feed.Open.Video?.Height ?? 0, _logger, () => _ = _camera.RequestKeyFrameAsync());
        Preview.SetMediaPlayer(_player.Player);
    });

    private void OnVirtualCameraChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(ApplyVirtualCamera);

    private async void OnInstallCameraClick(object sender, RoutedEventArgs e)
    {
        InstallCamera.IsEnabled = false;
        _installFailed = !await _virtualCamera.InstallAsync();
        InstallCamera.IsEnabled = true;
        ApplyVirtualCamera();
    }

    private async void OnRemoveCameraClick(object sender, RoutedEventArgs e)
    {
        RemoveCamera.IsEnabled = false;
        await _virtualCamera.UninstallAsync();
        RemoveCamera.IsEnabled = true;
        ApplyVirtualCamera();
    }

    private void ApplyVirtualCamera()
    {
        var installed = _virtualCamera.IsInstalled;
        var update = installed && _virtualCamera.NeedsUpdate;
        InUseBadge.Visibility = _virtualCamera.InUse ? Visibility.Visible : Visibility.Collapsed;
        RemoveCamera.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        InstallCamera.Visibility = !installed || update ? Visibility.Visible : Visibility.Collapsed;
        InstallCamera.Content = _localization.GetString(update ? "VirtualCamera_Update" : "VirtualCamera_Install");
        VirtualCameraText.Text = _localization.GetString(
            _installFailed && !installed ? "VirtualCamera_Failed"
            : !installed ? "VirtualCamera_NotInstalled"
            : update ? "VirtualCamera_NeedsUpdate"
            : _virtualCamera.InUse ? "VirtualCamera_InUse"
            : "VirtualCamera_Installed");
    }

    private void ApplyState()
    {
        var state = _camera.State;
        var live = state == CameraState.Streaming;
        PreviewState.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        PreviewStop.Visibility = live || state is CameraState.WaitingForTap or CameraState.Starting ? Visibility.Visible : Visibility.Collapsed;
        PreviewStart.Visibility = state is CameraState.Stopped or CameraState.Failed or CameraState.Unspecified ? Visibility.Visible : Visibility.Collapsed;
        PreviewText.Text = _localization.GetString(state switch
        {
            CameraState.WaitingForTap => "Camera_WaitingTap",
            CameraState.Starting => "Camera_Starting",
            CameraState.Failed when _camera.Unsupported => "Camera_NotOffered",
            CameraState.Failed => "Camera_Failed",
            _ => "Camera_Idle",
        });

        if (!live && state is CameraState.Stopped or CameraState.Failed)
        {
            _player?.Dispose();
            _player = null;
            Preview.SetMediaPlayer(null);
        }
    }
}
