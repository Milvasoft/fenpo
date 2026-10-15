using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.Core.Devices;
using Fenpo.Core.Settings;
using Microsoft.UI.Dispatching;
using Windows.Devices.Radios;

namespace Fenpo.App.ViewModels;

/// <summary>
/// State of one step or check in the pairing wizard.
/// </summary>
public enum StepState
{
    /// <summary>Not reached yet.</summary>
    Pending,

    /// <summary>The step to do now.</summary>
    Current,

    /// <summary>Done.</summary>
    Done,
}

/// <summary>
/// First-run pairing without the iPhone app (DECISIONS #28, design "İlk kurulum"): Windows Bluetooth pairing, the
/// iPhone's notification sharing prompt and message access, with a live checklist and a Phone Link warning.
/// </summary>
public sealed partial class PairingViewModel : ObservableObject, IDisposable
{
    private readonly IPhoneStatusSource _status;
    private readonly ISettingsStore _settings;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _poll;

    /// <summary>
    /// Initializes a new instance of the <see cref="PairingViewModel"/> class.
    /// </summary>
    /// <param name="status">Phone status.</param>
    /// <param name="settings">Settings store.</param>
    public PairingViewModel(IPhoneStatusSource status, ISettingsStore settings)
    {
        _status = status;
        _settings = settings;
        _status.Changed += OnStatusChanged;

        _poll = _dispatcher.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(2);
        _poll.Tick += (_, _) => _ = RefreshEnvironmentAsync();
        _poll.Start();

        Apply(_status.Current);
        _ = RefreshEnvironmentAsync();
    }

    /// <summary>
    /// Raised when the wizard should close (done or skipped).
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>Gets or sets step 1 (iPhone Bluetooth screen).</summary>
    [ObservableProperty]
    public partial StepState OpenBluetoothStep { get; set; }

    /// <summary>Gets or sets step 2 (pair in Windows).</summary>
    [ObservableProperty]
    public partial StepState PairStep { get; set; }

    /// <summary>Gets or sets step 3 (allow notification sharing).</summary>
    [ObservableProperty]
    public partial StepState NotificationsStep { get; set; }

    /// <summary>Gets or sets step 4 (allow messages).</summary>
    [ObservableProperty]
    public partial StepState MessagesStep { get; set; }

    /// <summary>Gets or sets a value indicating whether Bluetooth is on.</summary>
    [ObservableProperty]
    public partial bool BluetoothOn { get; set; }

    /// <summary>Gets or sets a value indicating whether the iPhone is paired and found.</summary>
    [ObservableProperty]
    public partial bool PhoneFound { get; set; }

    /// <summary>Gets or sets a value indicating whether notifications flow.</summary>
    [ObservableProperty]
    public partial bool NotificationsOk { get; set; }

    /// <summary>Gets or sets a value indicating whether messages are accessible.</summary>
    [ObservableProperty]
    public partial bool MessagesOk { get; set; }

    /// <summary>Gets or sets a value indicating whether Phone Link is running.</summary>
    [ObservableProperty]
    public partial bool PhoneLinkRunning { get; set; }

    /// <summary>Gets or sets a value indicating whether the bond looks broken.</summary>
    [ObservableProperty]
    public partial bool BrokenBond { get; set; }

    /// <summary>Gets or sets the found phone's name.</summary>
    [ObservableProperty]
    public partial string PhoneName { get; set; } = string.Empty;

    /// <inheritdoc/>
    public void Dispose()
    {
        _poll.Stop();
        _status.Changed -= OnStatusChanged;
    }

    [RelayCommand]
    private static async Task OpenBluetoothSettingsAsync() => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));

    [RelayCommand]
    private void ClosePhoneLink()
    {
        foreach (var process in Process.GetProcessesByName("PhoneExperienceHost"))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }

        PhoneLinkRunning = false;
    }

    [RelayCommand]
    private void Later()
    {
        _settings.Update(s => s with { SetupCompleted = true });
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnStatusChanged(object? sender, PhoneStatus status) => _dispatcher.TryEnqueue(() => Apply(status));

    private void Apply(PhoneStatus status)
    {
        PhoneFound = status.State != PhoneConnectionState.NotPaired;
        PhoneName = status.Name;
        NotificationsOk = status.NotificationsAvailable;
        MessagesOk = status.MessagesAvailable;
        BrokenBond = status.State == PhoneConnectionState.BrokenBond;

        OpenBluetoothStep = PhoneFound ? StepState.Done : StepState.Current;
        PairStep = PhoneFound ? StepState.Done : StepState.Current;
        NotificationsStep = NotificationsOk ? StepState.Done : PhoneFound ? StepState.Current : StepState.Pending;
        MessagesStep = MessagesOk ? StepState.Done : NotificationsOk ? StepState.Current : StepState.Pending;

        // Messages are optional; once the phone is paired and notifications flow the wizard is done.
        if (PhoneFound && NotificationsOk && !_settings.Current.SetupCompleted)
        {
            _settings.Update(s => s with { SetupCompleted = true });
            var timer = _dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
            timer.Start();
        }
    }

    private async Task RefreshEnvironmentAsync()
    {
        PhoneLinkRunning = Process.GetProcessesByName("PhoneExperienceHost").Length > 0;

        try
        {
            var radios = await Radio.GetRadiosAsync();
            BluetoothOn = radios.Any(r => r.Kind == RadioKind.Bluetooth && r.State == RadioState.On);
        }
        catch (UnauthorizedAccessException)
        {
            BluetoothOn = true;
        }
    }
}
