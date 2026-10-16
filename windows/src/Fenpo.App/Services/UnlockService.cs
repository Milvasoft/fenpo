using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Fenpo.Protocol.V1;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Storage;

namespace Fenpo.App.Services;

/// <summary>
/// How setting up unlocking with the phone ended.
/// </summary>
public enum UnlockSetupResult
{
    /// <summary>Set up.</summary>
    Done,

    /// <summary>Windows did not accept the password.</summary>
    WrongPassword,

    /// <summary>Fenpo on the phone is not connected or does not offer unlocking.</summary>
    NotAvailable,

    /// <summary>The phone's user did not approve.</summary>
    Declined,

    /// <summary>The phone has no strong biometrics, or something else failed.</summary>
    Failed,
}

/// <summary>
/// Unlocking the locked session with the phone (unlock.proto): keeps the Windows password encrypted under a key only
/// the phone has, and serves Fenpo's credential provider in LogonUI over a pipe only SYSTEM may open, answering only
/// LogonUI.exe of this session while it is locked. Also installs the credential provider with administrator approval.
/// </summary>
public sealed partial class UnlockService(PhoneChannel channel, ILogger<UnlockService> logger) : IHostedService
{
    private const string _classId = "{3B6E2B7A-9C41-4D8E-B2F0-6A1C5D7E8F90}";
    private const string _dllName = "Fenpo.CredentialProvider.dll";
    private static readonly byte[] _entropy = "fenpo-unlock"u8.ToArray();
    private static readonly TimeSpan _answerTimeout = TimeSpan.FromSeconds(60);

    private readonly PhoneChannel _channel = channel;
    private readonly ILogger<UnlockService> _logger = logger;
    private readonly string _path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "unlock.bin");
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private Pending? _pending;

    /// <summary>
    /// Raised from any thread when setup or the installed component changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value indicating whether unlocking is set up for this user.
    /// </summary>
    public bool IsSetUp => File.Exists(_path);

    /// <summary>
    /// Gets a value indicating whether the credential provider is installed.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{_classId}");
            return key is not null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the phone in use can unlock the PC now.
    /// </summary>
    public bool PhoneOffersUnlock => _channel.IsConnected && _channel.Supports(Capability.Unlock);

    private static string PackagedPath => Path.Combine(AppContext.BaseDirectory, _dllName);

    private static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Fenpo", "Unlock");

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _channel.MessageReceived += (sender, message) => OnMessage(message);
        _ = Task.Run(() => ServeAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => _stopping.CancelAsync();

    /// <summary>
    /// Sets unlocking up: checks the password with Windows, asks the phone for a key (Face ID or the fingerprint there)
    /// and keeps the password encrypted under it.
    /// </summary>
    /// <param name="password">The Windows password.</param>
    /// <returns>How it ended.</returns>
    public async Task<UnlockSetupResult> SetUpAsync(string password)
    {
        if (!PhoneOffersUnlock)
        {
            return UnlockSetupResult.NotAvailable;
        }

        if (!CheckPassword(password))
        {
            return UnlockSetupResult.WrongPassword;
        }

        var answer = await AskPhoneAsync(new Envelope { UnlockEnroll = new UnlockEnroll { PcName = Environment.MachineName } }, string.Empty);

        if (answer.Key is not { Length: 32 } key)
        {
            return answer.Reason == "declined" ? UnlockSetupResult.Declined : answer.Reason == "notconnected" ? UnlockSetupResult.NotAvailable : UnlockSetupResult.Failed;
        }

        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plain = Encoding.UTF8.GetBytes(password);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, plain, cipher, tag);
            }

            CryptographicOperations.ZeroMemory(plain);
            var stored = new Stored(WindowsIdentity.GetCurrent().User!.Value, nonce, cipher, tag);
            File.WriteAllBytes(_path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(stored), _entropy, DataProtectionScope.CurrentUser));
            _logger.LogInformation("Unlocking with the phone is set up");
            Changed?.Invoke(this, EventArgs.Empty);
            return UnlockSetupResult.Done;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Turns unlocking off: the phone forgets its key, the PC its encrypted password.
    /// </summary>
    /// <returns>A task that completes when the phone was told.</returns>
    public async Task TurnOffAsync()
    {
        File.Delete(_path);
        await _channel.SendAsync(new Envelope { UnlockRevoke = new UnlockRevoke() });
        _logger.LogInformation("Unlocking with the phone is off");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Installs the credential provider; Windows asks for administrator approval.
    /// </summary>
    /// <returns>True when installed.</returns>
    public async Task<bool> InstallAsync()
    {
        var target = Path.Combine(InstallFolder, _dllName);
        var command = string.Join(" & ",
            $"mkdir \"{InstallFolder}\" 2>nul",
            $"copy /y \"{PackagedPath}\" \"{target}\" >nul",
            $"regsvr32 /s \"{target}\"");
        await RunElevatedAsync(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return IsInstalled;
    }

    /// <summary>
    /// Removes the credential provider; Windows asks for administrator approval.
    /// </summary>
    /// <returns>True when removed.</returns>
    public async Task<bool> UninstallAsync()
    {
        var target = Path.Combine(InstallFolder, _dllName);
        await RunElevatedAsync(string.Join(" & ", $"regsvr32 /u /s \"{target}\"", $"del /f /q \"{target}\" 2>nul", $"rmdir \"{InstallFolder}\" 2>nul"));
        Changed?.Invoke(this, EventArgs.Empty);
        return !IsInstalled;
    }

    private void OnMessage(ChannelMessage message)
    {
        if (_pending is not { } pending)
        {
            return;
        }

        switch (message.Envelope.BodyCase)
        {
            case Envelope.BodyOneofCase.UnlockKey when message.Envelope.UnlockKey.RequestId == pending.RequestId:
                pending.Answer.TrySetResult(new PhoneAnswer(message.Envelope.UnlockKey.Key.ToByteArray(), string.Empty));
                break;
            case Envelope.BodyOneofCase.Error when message.Envelope.ReplyTo != 0 && message.Envelope.ReplyTo == pending.Request.Id:
                pending.Answer.TrySetResult(new PhoneAnswer(null, message.Envelope.Error.Code switch
                {
                    ErrorCode.Declined => "declined",
                    ErrorCode.NotPaired => "notpaired",
                    _ => "error",
                }));
                break;
        }
    }

    private async Task<PhoneAnswer> AskPhoneAsync(Envelope request, string requestId)
    {
        await _oneAtATime.WaitAsync();

        try
        {
            var pending = new Pending(request, requestId, new TaskCompletionSource<PhoneAnswer>(TaskCreationOptions.RunContinuationsAsynchronously));
            _pending = pending;

            if (!await _channel.SendAsync(request))
            {
                return new PhoneAnswer(null, "notconnected");
            }

            return await pending.Answer.Task.WaitAsync(_answerTimeout);
        }
        catch (TimeoutException)
        {
            return new PhoneAnswer(null, "timeout");
        }
        finally
        {
            _pending = null;
            _oneAtATime.Release();
        }
    }

    // LogonUI's credential provider asks here; each request is a line, each answer a line (see UnlockPipe.h).
    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        var name = $"fenpo-unlock-{Process.GetCurrentProcess().SessionId}";
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
                await pipe.WaitForConnectionAsync(cancellationToken);

                if (!FromLogonUi(pipe))
                {
                    _logger.LogWarning("Unlock pipe: a client other than LogonUI was turned away");
                    continue;
                }

                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                var line = await reader.ReadLineAsync(cancellationToken);
                await writer.WriteLineAsync(await AnswerAsync(line));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogWarning(exception, "Unlock pipe failed");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task<string> AnswerAsync(string? request)
    {
        switch (request)
        {
            case "STATUS":
                return Load() is { } stored && SessionLocked() ? $"ENROLLED {stored.Sid}" : "NONE";
            case "UNLOCK":
                if (!SessionLocked() || Load() is not { } secret)
                {
                    return "FAIL error";
                }

                _logger.LogInformation("The lock screen asks to unlock with the phone");
                var requestId = Guid.NewGuid().ToString("N");
                var answer = await AskPhoneAsync(new Envelope { UnlockRequest = new UnlockRequest { PcName = Environment.MachineName, RequestId = requestId } }, requestId);

                if (answer.Key is not { Length: 32 } key)
                {
                    if (answer.Reason == "notpaired")
                    {
                        // The phone lost the key (new fingerprints, removed there): set up again.
                        File.Delete(_path);
                        Changed?.Invoke(this, EventArgs.Empty);
                    }

                    _logger.LogInformation("Unlock not approved: {Reason}", answer.Reason);
                    return $"FAIL {answer.Reason}";
                }

                var plain = new byte[secret.Cipher.Length];

                try
                {
                    using var aes = new AesGcm(key, 16);
                    aes.Decrypt(secret.Nonce, secret.Cipher, secret.Tag, plain);
                    _logger.LogInformation("Unlock approved on the phone");
                    return $"OK {Convert.ToBase64String(plain)}";
                }
                catch (CryptographicException)
                {
                    File.Delete(_path);
                    Changed?.Invoke(this, EventArgs.Empty);
                    return "FAIL notpaired";
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                    CryptographicOperations.ZeroMemory(key);
                }

            default:
                return "FAIL error";
        }
    }

    private Stored? Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<Stored>(ProtectedData.Unprotect(File.ReadAllBytes(_path), _entropy, DataProtectionScope.CurrentUser)) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or CryptographicException)
        {
            _logger.LogWarning(exception, "The unlock data could not be read");
            return null;
        }
    }

    // Only LogonUI in this session may get a password.
    private static unsafe bool FromLogonUi(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var processId)
            || !ProcessIdToSessionId(processId, out var session) || session != Process.GetCurrentProcess().SessionId)
        {
            return false;
        }

        var process = OpenProcess(0x1000, false, processId);

        if (process == 0)
        {
            return false;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var length = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, ref length)
                && string.Equals(new string(buffer, 0, length), Path.Combine(Environment.SystemDirectory, "LogonUI.exe"), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    // WTS_SESSIONSTATE_LOCK (0) in WTSINFOEX level 1 (Windows 10 and later report it the right way round).
    private static unsafe bool SessionLocked()
    {
        if (!WTSQuerySessionInformationW(0, -1, 25, out var buffer, out _))
        {
            return false;
        }

        try
        {
            return *(int*)(buffer + 16) == 0;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    // Windows checks the password, so a typo is caught now and not on the lock screen.
    private bool CheckPassword(string password)
    {
        if (LogonUserW(Environment.UserName, Environment.UserDomainName, password, 2, 0, out var token))
        {
            CloseHandle(token);
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        _logger.LogInformation("Windows checked the password: error {Error}", error);

        // Only a plain "wrong user name or password" is a no; other answers (Microsoft account, policies) are not.
        return error != 1326;
    }

    private async Task RunElevatedAsync(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });

            if (process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            _logger.LogInformation("The unlock component install was not approved");
        }
    }

    private sealed record Stored(string Sid, byte[] Nonce, byte[] Cipher, byte[] Tag);

    private sealed record Pending(Envelope Request, string RequestId, TaskCompletionSource<PhoneAnswer> Answer);

    private sealed record PhoneAnswer(byte[]? Key, string Reason);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(nint pipe, out uint processId);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProcessIdToSessionId(uint processId, out int sessionId);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref int size);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("wtsapi32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformationW(nint server, int session, int infoClass, out nint buffer, out uint bytes);

    [LibraryImport("wtsapi32")]
    private static partial void WTSFreeMemory(nint memory);

    [LibraryImport("advapi32", EntryPoint = "LogonUserW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LogonUserW(string user, string domain, string password, int logonType, int provider, out nint token);
}
