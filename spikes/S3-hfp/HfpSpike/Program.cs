using System.Text;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

// Run from a terminal through the package's execution alias (FenpoS3) so the process has package identity.
// Claims the hands-free role for the paired iPhone, watches phone lines and calls, and offers call control.
Console.OutputEncoding = Encoding.UTF8;

var lines = new Dictionary<Guid, PhoneLine>();
var knownCalls = new Dictionary<string, PhoneCall>();
PhoneCall? current = null;

Log("main", $"Base directory: {AppContext.BaseDirectory}");

try
{
    Log("main", $"Package identity: {Package.Current.Id.FullName}");
}
catch (Exception exception)
{
    Log("main", $"No package identity ({exception.HResult:X8}). Start through the FenpoS3 alias.");
}

// 1. Transport: the iPhone's HFP audio gateway as seen by Windows.
var selector = PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth);
var transports = await DeviceInformation.FindAllAsync(selector);
Log("transport", $"{transports.Count} phone line transport(s)");

foreach (var info in transports)
{
    Log("transport", $"  {info.Name} [{info.Id}]");
}

var nameFilter = args.Length > 0 ? args[0] : "iphone";
var chosen = transports.FirstOrDefault(t => t.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) ?? transports.FirstOrDefault();

if (chosen is null)
{
    Log("transport", "No transport. Is the iPhone paired over classic Bluetooth?");
    return 1;
}

var transport = PhoneLineTransportDevice.FromId(chosen.Id);
Log("transport", $"Chosen {chosen.Name}, IsRegistered={transport.IsRegistered()}");

TryLog("transport", () => $"AudioRoutingStatus={transport.AudioRoutingStatus}, InBandRingingEnabled={transport.InBandRingingEnabled}");
TryRun("transport", () => transport.AudioRoutingStatusChanged += (sender, _) => Log("transport", $"AudioRoutingStatus changed: {sender.AudioRoutingStatus}"));
TryRun("transport", () => transport.InBandRingingEnabledChanged += (sender, _) => Log("transport", $"InBandRingingEnabled changed: {sender.InBandRingingEnabled}"));

// Diagnostic only: an invalid token cannot unlock anything, the status says whether a gate exists.
TryLog("transport", () => $"LAF phonelinetransportdevice_v1: {LimitedAccessFeatures.TryUnlockFeature("com.microsoft.windows.applicationmodel.phonelinetransportdevice_v1", "FENPO-PROBE-NOT-A-TOKEN", "Fenpo probe").Status}");

var access = await transport.RequestAccessAsync();
Log("transport", $"RequestAccessAsync: {access}");

if (!transport.IsRegistered())
{
    Log("transport", "RegisterApp...");
    TryRun("transport", transport.RegisterApp);
    Log("transport", $"IsRegistered={transport.IsRegistered()}");
}

if (!transport.IsRegistered())
{
    var users = await Windows.System.User.FindAllAsync();
    Log("transport", $"RegisterAppForUser ({users.Count} user(s))...");

    foreach (var user in users)
    {
        TryRun("transport", () => transport.RegisterAppForUser(user));
    }

    await Task.Delay(1000);
    Log("transport", $"IsRegistered={transport.IsRegistered()}");
}

Log("transport", "ConnectAsync...");
var connected = await transport.ConnectAsync();
Log("transport", $"ConnectAsync returned {connected}");

// 2. Phone lines: Windows creates a line for the HFP transport once the role is claimed.
var store = await PhoneCallManager.RequestStoreAsync();
var watcher = store.RequestLineWatcher();
watcher.LineAdded += async (_, args) => await OnLineAsync("added", args.LineId);
watcher.LineUpdated += async (_, args) => await OnLineAsync("updated", args.LineId);
watcher.LineRemoved += (_, args) => Log("line", $"removed {args.LineId}");
watcher.EnumerationCompleted += (_, _) => Log("line", "enumeration completed");
watcher.Stopped += (_, _) => Log("line", "watcher stopped");
watcher.Start();

// 3. Calls.
PhoneCallManager.CallStateChanged += async (_, _) =>
{
    Log("call", $"CallStateChanged: IsCallActive={PhoneCallManager.IsCallActive}, IsCallIncoming={PhoneCallManager.IsCallIncoming}");
    await RefreshCallsAsync();
};

Log("main", "Commands: status, register, lines, calls, dial <number>, answer, reject, end, mute, unmute, local, remote, q");

while (Console.ReadLine() is { } input)
{
    var parts = input.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length == 0)
    {
        continue;
    }

    try
    {
        switch (parts[0])
        {
            case "q":
                transport.UnregisterApp();
                Log("transport", $"Unregistered, IsRegistered={transport.IsRegistered()}");
                return 0;
            case "status":
                Log("transport", $"IsRegistered={transport.IsRegistered()}, AudioRoutingStatus={transport.AudioRoutingStatus}");
                break;
            case "register":
                transport.RegisterApp();
                Log("transport", $"RegisterApp done, IsRegistered={transport.IsRegistered()}");
                Log("transport", $"ConnectAsync returned {await transport.ConnectAsync()}");
                break;
            case "lines":
                foreach (var line in lines.Values)
                {
                    DescribeLine(line);
                }

                break;
            case "calls":
                await RefreshCallsAsync();
                break;
            case "dial" when parts.Length > 1:
                await DialAsync(parts[1].Trim());
                break;
            case "answer" when current is not null:
                Log("call", $"AcceptIncomingAsync: {await current.AcceptIncomingAsync()}");
                break;
            case "reject" when current is not null:
                Log("call", $"RejectIncomingAsync: {await current.RejectIncomingAsync()}");
                break;
            case "end" when current is not null:
                Log("call", $"EndAsync: {await current.EndAsync()}");
                break;
            case "mute" when current is not null:
                Log("call", $"MuteAsync: {await current.MuteAsync()}");
                break;
            case "unmute" when current is not null:
                Log("call", $"UnmuteAsync: {await current.UnmuteAsync()}");
                break;
            case "local" when current is not null:
                Log("call", $"ChangeAudioDevice(LocalDevice): {await current.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice)}");
                break;
            case "remote" when current is not null:
                Log("call", $"ChangeAudioDevice(RemoteDevice): {await current.ChangeAudioDeviceAsync(PhoneCallAudioDevice.RemoteDevice)}");
                break;
            default:
                Log("main", current is null && parts[0] is "answer" or "reject" or "end" or "mute" or "unmute" or "local" or "remote"
                    ? "No call yet"
                    : $"Unknown command: {input}");
                break;
        }
    }
    catch (Exception exception)
    {
        Log("main", $"{exception.GetType().Name} 0x{exception.HResult:X8}: {exception.Message}");
    }
}

return 0;

async Task OnLineAsync(string what, Guid lineId)
{
    var line = await PhoneLine.FromIdAsync(lineId);

    if (line is null)
    {
        Log("line", $"{what} {lineId}: FromIdAsync returned null");
        return;
    }

    lines[lineId] = line;
    Log("line", $"{what}:");
    DescribeLine(line);
}

void DescribeLine(PhoneLine line)
{
    Log("line", $"  id={line.Id} name=\"{line.DisplayName}\" transport={line.Transport} transportDevice={line.TransportDeviceId}");
    Log("line", $"  canDial={line.CanDial} network=\"{line.NetworkName}\" state={line.NetworkState} video={line.VideoCallingCapabilities?.IsVideoCallingCapable}");
    Log("line", $"  ours={string.Equals(line.TransportDeviceId, chosen.Id, StringComparison.OrdinalIgnoreCase)}");
}

async Task RefreshCallsAsync()
{
    foreach (var line in lines.Values)
    {
        var result = await line.GetAllActivePhoneCallsAsync();
        Log("call", $"line \"{line.DisplayName}\": {result.OperationStatus}, {result.AllActivePhoneCalls.Count} active");

        foreach (var call in result.AllActivePhoneCalls)
        {
            await TrackAsync(call);
        }
    }
}

async Task TrackAsync(PhoneCall call)
{
    var info = await call.GetPhoneCallInfoAsync();
    Log("call", $"  {call.CallId} status={call.Status} audio={call.AudioDevice} muted={call.IsMuted} direction={info.CallDirection} number=\"{info.PhoneNumber}\" name=\"{info.DisplayName}\" start={info.StartTime:HH:mm:ss}");

    if (knownCalls.TryAdd(call.CallId, call))
    {
        call.StatusChanged += (sender, _) => Log("call", $"{sender.CallId} status -> {sender.Status}");
        call.AudioDeviceChanged += (sender, _) => Log("call", $"{sender.CallId} audio -> {sender.AudioDevice}");
        call.IsMutedChanged += (sender, _) => Log("call", $"{sender.CallId} muted -> {sender.IsMuted}");
    }

    current = call;
}

async Task DialAsync(string number)
{
    var line = lines.Values.FirstOrDefault(l => string.Equals(l.TransportDeviceId, chosen.Id, StringComparison.OrdinalIgnoreCase))
        ?? lines.Values.FirstOrDefault(l => l.CanDial);

    if (line is null)
    {
        Log("call", "No line to dial on");
        return;
    }

    Log("call", $"Dialing {number} on \"{line.DisplayName}\"");
    var result = await line.DialWithResultAsync(number, number);
    Log("call", $"DialWithResultAsync: {result.DialCallStatus}, call={result.DialedCall?.CallId}");

    if (result.DialedCall is { } call)
    {
        await TrackAsync(call);
    }
}

static void Log(string area, string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{area}] {message}");

static void TryLog(string area, Func<string> message)
{
    try
    {
        Log(area, message());
    }
    catch (Exception exception)
    {
        Log(area, $"{exception.GetType().Name} 0x{exception.HResult:X8}: {exception.Message}");
    }
}

static void TryRun(string area, Action action)
{
    try
    {
        action();
    }
    catch (Exception exception)
    {
        Log(area, $"{exception.GetType().Name} 0x{exception.HResult:X8}: {exception.Message}");
    }
}
