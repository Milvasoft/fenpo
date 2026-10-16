using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fenpo.App.Services;
using Microsoft.UI.Dispatching;

namespace Fenpo.App.ViewModels;

/// <summary>
/// Files page: sending files to the phone and the list of transfers both ways.
/// </summary>
public sealed partial class FilesViewModel : ObservableObject, IDisposable
{
    private readonly FileTransferService _transfers;
    private readonly LocalizationService _localization;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _refresh;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilesViewModel"/> class.
    /// </summary>
    /// <param name="transfers">File transfers.</param>
    /// <param name="localization">Strings.</param>
    public FilesViewModel(FileTransferService transfers, LocalizationService localization)
    {
        _transfers = transfers;
        _localization = localization;

        // Progress arrives many times a second; the list follows four times a second.
        _refresh = _dispatcher.CreateTimer();
        _refresh.Interval = TimeSpan.FromMilliseconds(250);
        _refresh.IsRepeating = false;
        _refresh.Tick += (sender, e) => Refresh();
        _transfers.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Gets the transfers.</summary>
    public ObservableCollection<TransferRowViewModel> Transfers { get; } = [];

    /// <summary>Gets or sets a value indicating whether there are transfers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasTransfers { get; set; }

    /// <summary>Gets a value indicating whether the empty text shows.</summary>
    public bool IsEmpty => !HasTransfers;

    /// <summary>Gets or sets why the last send did not start, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether <see cref="Notice"/> shows.</summary>
    public bool HasNotice => Notice.Length > 0;

    /// <summary>
    /// Formats a byte count ("2,4 MB").
    /// </summary>
    /// <param name="bytes">Bytes.</param>
    /// <param name="localization">Strings.</param>
    /// <returns>The size text.</returns>
    public static string Size(ulong bytes, LocalizationService localization) => bytes switch
    {
        < 1024 => localization.Format("Size_Bytes", bytes),
        < 1024 * 1024 => localization.Format("Size_KB", Math.Round(bytes / 1024.0, 0).ToString("0", CultureInfo.CurrentCulture)),
        < 1024ul * 1024 * 1024 => localization.Format("Size_MB", (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture)),
        _ => localization.Format("Size_GB", (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.CurrentCulture)),
    };

    /// <summary>
    /// Sends files to the phone and says why when it cannot.
    /// </summary>
    /// <param name="paths">Files.</param>
    /// <returns>A task that completes when the offer went out or failed.</returns>
    public async Task SendAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var result = await _transfers.SendAsync(paths);
        Notice = result switch
        {
            SendFilesResult.Started => string.Empty,
            SendFilesResult.NothingToSend => _localization.GetString("Files_NothingToSend"),
            SendFilesResult.Unsupported => _localization.GetString("Files_Unsupported"),
            SendFilesResult.NeedsWifi => _localization.GetString("Files_NeedsWifi"),
            _ => _localization.GetString("Files_NotConnected"),
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _refresh.Stop();
        _transfers.Changed -= OnChanged;
    }

    [RelayCommand]
    private void OpenFolder() => _transfers.Reveal(null);

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        if (!_refresh.IsRunning)
        {
            _refresh.Start();
        }
    });

    private void Refresh()
    {
        var current = _transfers.Transfers;

        // Rows stay in place so buttons keep focus while progress moves.
        for (var i = 0; i < current.Count; i++)
        {
            if (i < Transfers.Count && Transfers[i].Transfer == current[i])
            {
                Transfers[i].Refresh();
                continue;
            }

            var existing = Transfers.FirstOrDefault(r => r.Transfer == current[i]);

            if (existing is not null)
            {
                Transfers.Remove(existing);
                existing.Refresh();
                Transfers.Insert(i, existing);
            }
            else
            {
                Transfers.Insert(i, new TransferRowViewModel(current[i], _transfers, this, _localization));
            }
        }

        while (Transfers.Count > current.Count)
        {
            Transfers.RemoveAt(Transfers.Count - 1);
        }

        HasTransfers = Transfers.Count > 0;
    }
}

/// <summary>
/// One transfer in the list.
/// </summary>
public sealed partial class TransferRowViewModel : ObservableObject
{
    private readonly FileTransferService _service;
    private readonly FilesViewModel? _owner;
    private readonly LocalizationService _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransferRowViewModel"/> class.
    /// </summary>
    /// <param name="transfer">Transfer.</param>
    /// <param name="service">Transfers (cancel, show).</param>
    /// <param name="owner">Files page (retry), or null on the home page, which shows no buttons.</param>
    /// <param name="localization">Strings.</param>
    public TransferRowViewModel(FileTransfer transfer, FileTransferService service, FilesViewModel? owner, LocalizationService localization)
    {
        Transfer = transfer;
        _service = service;
        _owner = owner;
        _localization = localization;
        var first = transfer.Files.Count > 0 ? transfer.Files[0].Name : string.Empty;
        Name = transfer.Files.Count > 1 ? localization.Format("Files_AndMore", first, transfer.Files.Count - 1) : first;
        var extension = Path.GetExtension(first).TrimStart('.').ToUpperInvariant();
        Extension = extension.Length is > 0 and <= 4 ? extension : "•••";
        Refresh();
    }

    /// <summary>Gets the transfer.</summary>
    public FileTransfer Transfer { get; }

    /// <summary>Gets the file name (and how many more).</summary>
    public string Name { get; }

    /// <summary>Gets the extension label on the file icon.</summary>
    public string Extension { get; }

    /// <summary>Gets when it was offered ("2 min", "Yesterday").</summary>
    public string When => NotificationRowViewModel.RelativeTime(Transfer.StartedAt, _localization);

    /// <summary>Gets or sets the line under the name.</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>Gets or sets the progress from 0 to 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    /// <summary>Gets or sets a value indicating whether the progress bar shows.</summary>
    [ObservableProperty]
    public partial bool ShowProgress { get; set; }

    /// <summary>Gets or sets the state text on the right ("Done"), or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether <see cref="Status"/> shows.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>Gets or sets the button text, or empty for no button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    public partial string ActionText { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the button shows.</summary>
    public bool HasAction => ActionText.Length > 0;

    /// <summary>
    /// Updates the texts from the transfer.
    /// </summary>
    public void Refresh()
    {
        var t = Transfer;
        var done = FilesViewModel.Size(t.BytesDone, _localization);
        var total = FilesViewModel.Size(t.TotalBytes, _localization);
        Detail = (t.State, t.Incoming) switch
        {
            (FileTransferState.Waiting, false) => _localization.GetString("Files_WaitingPhone"),
            (FileTransferState.Waiting, true) => _localization.GetString("Files_WaitingData"),
            (FileTransferState.InProgress, false) => _localization.Format("Files_Sending", done, total),
            (FileTransferState.InProgress, true) => _localization.Format("Files_Receiving", done, total),
            (FileTransferState.Completed, false) => _localization.Format("Files_Sent", total),
            (FileTransferState.Completed, true) => _localization.Format("Files_Received", total),
            (FileTransferState.Declined, _) => _localization.GetString("Files_Declined"),
            (FileTransferState.Cancelled, _) => _localization.GetString("Files_Cancelled"),
            (_, false) => _localization.GetString("Files_SendFailed"),
            _ => _localization.GetString("Files_ReceiveFailed"),
        };
        ShowProgress = t.State is FileTransferState.InProgress;
        Progress = t.TotalBytes == 0 ? 0 : t.BytesDone * 100.0 / t.TotalBytes;
        Status = t.State == FileTransferState.Completed ? _localization.GetString("Files_Done") : string.Empty;
        // A sent file has no button: "Retry" next to a finished transfer reads as a failure.
        ActionText = t.State switch
        {
            FileTransferState.Waiting or FileTransferState.InProgress => _localization.GetString("Files_Cancel"),
            FileTransferState.Completed => t.Incoming ? _localization.GetString("Files_Show") : string.Empty,
            _ when !t.Incoming && t.SourcePaths.All(File.Exists) => _localization.GetString("Files_Retry"),
            _ => string.Empty,
        };
    }

    [RelayCommand]
    private async Task ActAsync()
    {
        switch (Transfer.State)
        {
            case FileTransferState.Waiting or FileTransferState.InProgress:
                await _service.CancelAsync(Transfer);
                break;
            case FileTransferState.Completed when Transfer.Incoming:
                _service.Reveal(Transfer.SavedPaths.FirstOrDefault());
                break;
            default:
                if (!Transfer.Incoming && _owner is not null)
                {
                    await _owner.SendAsync(Transfer.SourcePaths);
                }

                break;
        }
    }
}
