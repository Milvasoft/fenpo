using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fenpo.App.Services;
using Fenpo.Core.Calls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Calls page: calls that rang while Fenpo was running.
/// </summary>
public sealed partial class CallsViewModel : ObservableObject, IDisposable
{
    private readonly CallService _calls;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// Initializes a new instance of the <see cref="CallsViewModel"/> class.
    /// </summary>
    /// <param name="calls">Call service with the recent calls.</param>
    /// <param name="localization">Strings.</param>
    public CallsViewModel(CallService calls, LocalizationService localization)
    {
        _calls = calls;
        _localization = localization;
        _calls.Log.Changed += OnChanged;
        Apply();
    }

    /// <summary>Gets the recent calls, newest first.</summary>
    public ObservableCollection<CallRowViewModel> Calls { get; } = [];

    /// <summary>Gets or sets a value indicating whether there are no calls yet.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <inheritdoc/>
    public void Dispose() => _calls.Log.Changed -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Apply);

    private void Apply()
    {
        Calls.Clear();

        foreach (var entry in _calls.Log.Entries)
        {
            Calls.Add(new CallRowViewModel(entry, _localization));
        }

        IsEmpty = Calls.Count == 0;
    }
}

/// <summary>
/// One recent call.
/// </summary>
public sealed class CallRowViewModel(CallEntry entry, LocalizationService localization)
{
    /// <summary>Gets the caller.</summary>
    public string Name { get; } = entry.Name;

    /// <summary>Gets the avatar letter.</summary>
    public string Initial { get; } = Avatar.Initial(entry.Name);

    /// <summary>Gets the avatar fill.</summary>
    public Brush Fill { get; } = Avatar.Fill(entry.Name);

    /// <summary>Gets "Incoming call" or "Missed call".</summary>
    public string Detail { get; } = localization.GetString(entry.Kind == CallKind.Missed ? "Calls_Missed" : "Calls_Incoming");

    /// <summary>Gets when it rang.</summary>
    public string Time { get; } = NotificationRowViewModel.RelativeTime(entry.Time, localization);

    /// <summary>Gets a value indicating whether it was missed (name in red, as in the design).</summary>
    public bool IsMissed { get; } = entry.Kind == CallKind.Missed;

    /// <summary>Gets a value indicating whether it was not missed.</summary>
    public bool IsNotMissed => !IsMissed;
}
