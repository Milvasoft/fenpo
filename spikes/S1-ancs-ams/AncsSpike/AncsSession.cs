using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Spikes.S1;

/// <summary>
/// Apple Notification Center Service client: Notification Source events, attribute requests on the
/// Control Point, reassembly of Data Source fragments, positive and negative actions.
/// </summary>
internal sealed class AncsSession(GattDeviceService service, CaptureLog capture)
{
    private const byte _commandGetNotificationAttributes = 0;
    private const byte _commandGetAppAttributes = 1;
    private const byte _commandPerformNotificationAction = 2;

    private static readonly string[] _categories = ["Other", "IncomingCall", "MissedCall", "Voicemail", "Social", "Schedule", "Email", "News", "HealthAndFitness", "BusinessAndFinance", "Location", "Entertainment"];
    private static readonly string[] _events = ["Added", "Modified", "Removed"];

    private readonly GattDeviceService _service = service;
    private readonly CaptureLog _capture = capture;
    private readonly SemaphoreSlim _controlPointGate = new(1, 1);
    private readonly Channel<uint> _attributeQueue = Channel.CreateUnbounded<uint>();
    private readonly List<byte> _dataSourceBuffer = [];
    private readonly Lock _bufferLock = new();
    private readonly Dictionary<string, string> _appNames = [];
    private readonly Dictionary<uint, DateTimeOffset> _receivedAt = [];
    private GattCharacteristic? _controlPoint;
    private PendingRequest? _pending;

    public int Added { get; private set; }

    public int Removed { get; private set; }

    public async Task<bool> StartAsync()
    {
        var notificationSource = await Gatt.GetCharacteristicAsync(_service, Uuids.AncsNotificationSource);
        _controlPoint = await Gatt.GetCharacteristicAsync(_service, Uuids.AncsControlPoint);
        var dataSource = await Gatt.GetCharacteristicAsync(_service, Uuids.AncsDataSource);

        if (notificationSource is null || _controlPoint is null || dataSource is null)
        {
            return false;
        }

        // Data Source first, so no attribute response is lost once events start flowing.
        if (!await Gatt.SubscribeAsync(dataSource, _capture, OnDataSource))
        {
            return false;
        }

        _ = Task.Run(ProcessAttributeQueueAsync);

        // The first subscription to Notification Source triggers the "Share notifications" prompt on the iPhone.
        return await Gatt.SubscribeAsync(notificationSource, _capture, OnNotificationSource);
    }

    public void Stop() => _attributeQueue.Writer.TryComplete();

    public async Task PerformActionAsync(uint uid, bool positive)
    {
        if (_controlPoint is null)
        {
            return;
        }

        var command = new byte[6];
        command[0] = _commandPerformNotificationAction;
        BitConverter.TryWriteBytes(command.AsSpan(1, 4), uid);
        command[5] = positive ? (byte)0 : (byte)1;

        await _controlPointGate.WaitAsync();

        try
        {
            var ok = await Gatt.WriteAsync(_controlPoint, command, _capture);
            Log.Info("ancs", $"{(positive ? "Positive" : "Negative")} action on uid={uid}: {(ok ? "sent" : "failed")}");
        }
        finally
        {
            _controlPointGate.Release();
        }
    }

    private void OnNotificationSource(byte[] data)
    {
        if (data.Length < 8)
        {
            Log.Warn("ancs", $"Short Notification Source value: {Convert.ToHexString(data)}");
            return;
        }

        var eventId = data[0];
        var flags = data[1];
        var category = data[2];
        var count = data[3];
        var uid = BitConverter.ToUInt32(data, 4);

        var eventName = eventId < _events.Length ? _events[eventId] : $"Event{eventId}";
        var categoryName = category < _categories.Length ? _categories[category] : $"Category{category}";
        Log.Info("ancs", $"{eventName} uid={uid} category={categoryName} count={count} flags={DescribeFlags(flags)}");

        if (eventId == 2)
        {
            Removed++;
            return;
        }

        if (eventId == 0)
        {
            Added++;
        }

        lock (_bufferLock)
        {
            _receivedAt[uid] = DateTimeOffset.Now;
        }

        _attributeQueue.Writer.TryWrite(uid);
    }

    private async Task ProcessAttributeQueueAsync()
    {
        await foreach (var uid in _attributeQueue.Reader.ReadAllAsync())
        {
            byte[] attributes = [0, 1, 2, 3, 4, 5, 6, 7];
            var command = new List<byte> { _commandGetNotificationAttributes };
            command.AddRange(BitConverter.GetBytes(uid));

            foreach (var attribute in attributes)
            {
                command.Add(attribute);

                // Title, Subtitle and Message take a 2 byte maximum length.
                if (attribute is 1 or 2 or 3)
                {
                    command.AddRange(BitConverter.GetBytes((ushort)(attribute == 3 ? 512 : 128)));
                }
            }

            var values = await RequestAsync(_commandGetNotificationAttributes, attributes, [.. command]);

            if (values is null)
            {
                continue;
            }

            var appId = Text(values, 0);
            var appName = await GetAppNameAsync(appId);
            DateTimeOffset receivedAt;

            lock (_bufferLock)
            {
                _receivedAt.Remove(uid, out receivedAt);
            }

            Log.Info("ancs", $"  uid={uid} app={appName} ({appId}) received {(DateTimeOffset.Now - receivedAt).TotalMilliseconds:F0} ms after the event");
            Log.Info("ancs", $"  title=\"{Text(values, 1)}\" subtitle=\"{Text(values, 2)}\" size={Text(values, 4)} date={Text(values, 5)}");
            Log.Info("ancs", $"  message=\"{Text(values, 3).ReplaceLineEndings(" / ")}\"");
            Log.Info("ancs", $"  positive=\"{Text(values, 6)}\" negative=\"{Text(values, 7)}\"");
        }
    }

    private async Task<string> GetAppNameAsync(string appId)
    {
        if (string.IsNullOrEmpty(appId))
        {
            return string.Empty;
        }

        if (_appNames.TryGetValue(appId, out var cached))
        {
            return cached;
        }

        var command = new List<byte> { _commandGetAppAttributes };
        command.AddRange(Encoding.UTF8.GetBytes(appId));
        command.Add(0);
        command.Add(0); // DisplayName

        var values = await RequestAsync(_commandGetAppAttributes, [0], [.. command]);
        var name = values is null ? appId : Text(values, 0);
        _appNames[appId] = name;
        return name;
    }

    private async Task<Dictionary<byte, byte[]>?> RequestAsync(byte commandId, byte[] attributes, byte[] command)
    {
        if (_controlPoint is null)
        {
            return null;
        }

        await _controlPointGate.WaitAsync();

        try
        {
            var pending = new PendingRequest(commandId, attributes);

            lock (_bufferLock)
            {
                _dataSourceBuffer.Clear();
                _pending = pending;
            }

            if (!await Gatt.WriteAsync(_controlPoint, command, _capture))
            {
                return null;
            }

            var completed = await Task.WhenAny(pending.Completion.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            if (completed != pending.Completion.Task)
            {
                Log.Warn("ancs", $"Timed out waiting for Data Source (command {commandId}), {_dataSourceBuffer.Count} bytes buffered");
                return null;
            }

            return pending.Completion.Task.Result;
        }
        finally
        {
            lock (_bufferLock)
            {
                _pending = null;
            }

            _controlPointGate.Release();
        }
    }

    private void OnDataSource(byte[] data)
    {
        lock (_bufferLock)
        {
            if (_pending is null)
            {
                Log.Warn("ancs", $"Data Source value without a request: {Convert.ToHexString(data)}");
                return;
            }

            _dataSourceBuffer.AddRange(data);

            if (TryParse(CollectionsMarshal.AsSpan(_dataSourceBuffer), _pending, out var values))
            {
                _dataSourceBuffer.Clear();
                _pending.Completion.TrySetResult(values);
            }
        }
    }

    private static bool TryParse(ReadOnlySpan<byte> buffer, PendingRequest pending, out Dictionary<byte, byte[]> values)
    {
        values = [];

        if (buffer.Length < 1 || buffer[0] != pending.CommandId)
        {
            return false;
        }

        int position;

        if (pending.CommandId == _commandGetNotificationAttributes)
        {
            position = 5; // CommandID + NotificationUID
        }
        else
        {
            var terminator = buffer[1..].IndexOf((byte)0);

            if (terminator < 0)
            {
                return false;
            }

            position = 1 + terminator + 1; // CommandID + AppIdentifier + NUL
        }

        foreach (var _ in pending.Attributes)
        {
            if (buffer.Length < position + 3)
            {
                return false;
            }

            var id = buffer[position];
            var length = buffer[position + 1] | (buffer[position + 2] << 8);

            if (buffer.Length < position + 3 + length)
            {
                return false;
            }

            values[id] = buffer.Slice(position + 3, length).ToArray();
            position += 3 + length;
        }

        if (position != buffer.Length)
        {
            Log.Warn("ancs", $"{buffer.Length - position} unexpected trailing bytes in Data Source response");
        }

        return true;
    }

    private static string Text(Dictionary<byte, byte[]> values, byte id) => values.TryGetValue(id, out var value) ? Encoding.UTF8.GetString(value) : string.Empty;

    private static string DescribeFlags(byte flags)
    {
        var names = new List<string>();

        if ((flags & 0x01) != 0)
        {
            names.Add("Silent");
        }

        if ((flags & 0x02) != 0)
        {
            names.Add("Important");
        }

        if ((flags & 0x04) != 0)
        {
            names.Add("PreExisting");
        }

        if ((flags & 0x08) != 0)
        {
            names.Add("PositiveAction");
        }

        if ((flags & 0x10) != 0)
        {
            names.Add("NegativeAction");
        }

        return names.Count == 0 ? "none" : string.Join("|", names);
    }

    private sealed class PendingRequest(byte commandId, byte[] attributes)
    {
        public byte CommandId { get; } = commandId;

        public byte[] Attributes { get; } = attributes;

        public TaskCompletionSource<Dictionary<byte, byte[]>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
