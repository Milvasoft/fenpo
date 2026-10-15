using CommunityToolkit.Mvvm.ComponentModel;
using Fenpo.Core.Devices;
using Fenpo.Core.Settings;
using Microsoft.UI.Dispatching;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Files, Clipboard and Camera pages: features that work through Fenpo on the iPhone. Until the app connects the
/// pages show what they need and keep their controls visible but inactive (DECISIONS #36).
/// </summary>
public sealed partial class AppFeatureViewModel : ObservableObject, IDisposable
{
    private readonly IPhoneStatusSource _status;
    private readonly ISettingsStore _settings;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly bool _loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppFeatureViewModel"/> class.
    /// </summary>
    /// <param name="status">Phone status (is the iPhone app connected).</param>
    /// <param name="settings">Settings store (clipboard sharing).</param>
    public AppFeatureViewModel(IPhoneStatusSource status, ISettingsStore settings)
    {
        _status = status;
        _settings = settings;
        _status.Changed += OnStatusChanged;
        AppConnected = status.Current.AppConnected;
        var current = settings.Current;
        ShareClipboard = current.ShareClipboard;
        LensIndex = (int)current.CameraLens;
        CenterStage = current.CameraCenterStage;
        PortraitBlur = current.CameraPortraitBlur;
        UseMicrophone = current.CameraUseMicrophone;
        _loaded = true;
    }

    /// <summary>Gets or sets a value indicating whether the iPhone app is connected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsApp), nameof(ContentOpacity))]
    public partial bool AppConnected { get; set; }

    /// <summary>Gets or sets a value indicating whether the clipboard is shared.</summary>
    [ObservableProperty]
    public partial bool ShareClipboard { get; set; }

    /// <summary>Gets or sets the camera lens: 0 wide, 1 ultra wide, 2 front.</summary>
    [ObservableProperty]
    public partial int LensIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether the camera keeps the person in the frame.</summary>
    [ObservableProperty]
    public partial bool CenterStage { get; set; }

    /// <summary>Gets or sets a value indicating whether the camera blurs the background.</summary>
    [ObservableProperty]
    public partial bool PortraitBlur { get; set; }

    /// <summary>Gets or sets a value indicating whether the iPhone microphone comes with the camera.</summary>
    [ObservableProperty]
    public partial bool UseMicrophone { get; set; }

    /// <summary>Gets a value indicating whether the needs-app banner is shown.</summary>
    public bool NeedsApp => !AppConnected;

    /// <summary>Gets the opacity of the feature's controls (dimmed until the app connects, as in the design).</summary>
    public double ContentOpacity => AppConnected ? 1 : 0.45;

    /// <inheritdoc/>
    public void Dispose() => _status.Changed -= OnStatusChanged;

    partial void OnShareClipboardChanged(bool value) => Save(s => s with { ShareClipboard = value });

    partial void OnLensIndexChanged(int value) => Save(s => s with { CameraLens = (CameraLens)value });

    partial void OnCenterStageChanged(bool value) => Save(s => s with { CameraCenterStage = value });

    partial void OnPortraitBlurChanged(bool value) => Save(s => s with { CameraPortraitBlur = value });

    partial void OnUseMicrophoneChanged(bool value) => Save(s => s with { CameraUseMicrophone = value });

    private void Save(Func<AppSettings, AppSettings> change)
    {
        if (_loaded)
        {
            _settings.Update(change);
        }
    }

    private void OnStatusChanged(object? sender, PhoneStatus status) => _dispatcher.TryEnqueue(() => AppConnected = status.AppConnected);
}
