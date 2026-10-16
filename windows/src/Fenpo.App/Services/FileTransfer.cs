namespace Fenpo.App.Services;

/// <summary>
/// Where a file transfer stands.
/// </summary>
public enum FileTransferState
{
    /// <summary>Offered, waiting for the other side to accept or for the phone to connect.</summary>
    Waiting,

    /// <summary>Bytes are moving.</summary>
    InProgress,

    /// <summary>Every file arrived.</summary>
    Completed,

    /// <summary>The phone said no.</summary>
    Declined,

    /// <summary>Stopped on either side.</summary>
    Cancelled,

    /// <summary>The connection broke or a file did not check out.</summary>
    Failed,
}

/// <summary>
/// One transfer of one or more files between the PC and the phone, as the Files page shows it.
/// </summary>
public sealed class FileTransfer
{
    private readonly Lock _lock = new();
    private ulong _bytesDone;
    private FileTransferState _state = FileTransferState.Waiting;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTransfer"/> class.
    /// </summary>
    /// <param name="id">Transfer id.</param>
    /// <param name="deviceId">Phone id.</param>
    /// <param name="incoming">True for phone to PC.</param>
    /// <param name="files">Files from the offer.</param>
    /// <param name="sourcePaths">Files on the PC for an outgoing transfer; empty for incoming.</param>
    public FileTransfer(string id, string deviceId, bool incoming, IReadOnlyList<Protocol.V1.FileInfo> files, IReadOnlyList<string> sourcePaths)
    {
        Id = id;
        DeviceId = deviceId;
        Incoming = incoming;
        Files = files;
        SourcePaths = sourcePaths;
        TotalBytes = files.Aggregate(0ul, (sum, f) => sum + f.Size);
    }

    /// <summary>
    /// Raised from any thread when the state or the progress changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Gets the transfer id.</summary>
    public string Id { get; }

    /// <summary>Gets when the transfer was offered.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    /// <summary>Gets the phone id.</summary>
    public string DeviceId { get; }

    /// <summary>Gets a value indicating whether the files come from the phone.</summary>
    public bool Incoming { get; }

    /// <summary>Gets the files from the offer.</summary>
    public IReadOnlyList<Protocol.V1.FileInfo> Files { get; }

    /// <summary>Gets the PC files of an outgoing transfer (for "Retry").</summary>
    public IReadOnlyList<string> SourcePaths { get; }

    /// <summary>Gets where the received files were saved.</summary>
    public IReadOnlyList<string> SavedPaths { get; private set; } = [];

    /// <summary>Gets the size of all files.</summary>
    public ulong TotalBytes { get; }

    /// <summary>Gets the bytes moved so far.</summary>
    public ulong BytesDone => Interlocked.Read(ref _bytesDone);

    /// <summary>Gets the state.</summary>
    public FileTransferState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>Gets a value indicating whether the transfer has ended.</summary>
    public bool IsFinished => State is not (FileTransferState.Waiting or FileTransferState.InProgress);

    /// <summary>Gets a value indicating whether the data connection was taken.</summary>
    internal bool Started { get; set; }

    /// <summary>
    /// Gets or sets the folder an incoming transfer is saved to; null for the incoming folder.
    /// </summary>
    internal string? Folder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the transfer stays out of the Files list and its notifications (a file
    /// the phone's Files app opened, a photo the PC asked for).
    /// </summary>
    internal bool Quiet { get; set; }

    /// <summary>Gets the cancellation source for the whole transfer.</summary>
    internal CancellationTokenSource Cancellation { get; } = new();

    /// <summary>Gets the phone's answer to an outgoing offer.</summary>
    internal TaskCompletionSource<bool> Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the end of the data connection.</summary>
    internal TaskCompletionSource DataDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the phone's final report on an outgoing transfer.</summary>
    internal TaskCompletionSource<Protocol.V1.TransferState> Confirmed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Moves to a new state unless the transfer already ended.
    /// </summary>
    /// <param name="state">New state.</param>
    /// <returns>True when the state changed.</returns>
    internal bool SetState(FileTransferState state)
    {
        lock (_lock)
        {
            if (_state == state || _state is not (FileTransferState.Waiting or FileTransferState.InProgress))
            {
                return false;
            }

            _state = state;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Records progress.
    /// </summary>
    /// <param name="bytes">Bytes moved so far.</param>
    internal void SetProgress(ulong bytes)
    {
        Interlocked.Exchange(ref _bytesDone, bytes);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records the saved files of an incoming transfer.
    /// </summary>
    /// <param name="paths">Paths.</param>
    internal void SetSaved(IReadOnlyList<string> paths) => SavedPaths = paths;
}
