using System.Text;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Fenpo.Spikes.S1;

/// <summary>
/// Apple Media Service client: registers for player, queue and track updates and sends remote commands.
/// </summary>
internal sealed class AmsSession(GattDeviceService service, CaptureLog capture)
{
    private static readonly string[] _entities = ["Player", "Queue", "Track"];
    private static readonly string[][] _attributes =
    [
        ["Name", "PlaybackInfo", "Volume"],
        ["Index", "Count", "ShuffleMode", "RepeatMode"],
        ["Artist", "Album", "Title", "Duration"],
    ];
    private static readonly string[] _commands = ["Play", "Pause", "TogglePlayPause", "NextTrack", "PreviousTrack", "VolumeUp", "VolumeDown", "AdvanceRepeatMode", "AdvanceShuffleMode", "SkipForward", "SkipBackward", "LikeTrack", "DislikeTrack", "BookmarkTrack"];

    private readonly GattDeviceService _service = service;
    private readonly CaptureLog _capture = capture;
    private GattCharacteristic? _remoteCommand;
    private GattCharacteristic? _entityAttribute;

    public string NowPlaying { get; private set; } = "(nothing)";

    public async Task<bool> StartAsync()
    {
        _remoteCommand = await Gatt.GetCharacteristicAsync(_service, Uuids.AmsRemoteCommand);
        var entityUpdate = await Gatt.GetCharacteristicAsync(_service, Uuids.AmsEntityUpdate);
        _entityAttribute = await Gatt.GetCharacteristicAsync(_service, Uuids.AmsEntityAttribute);

        if (_remoteCommand is null || entityUpdate is null)
        {
            return false;
        }

        await Gatt.SubscribeAsync(_remoteCommand, _capture, OnRemoteCommand);

        if (!await Gatt.SubscribeAsync(entityUpdate, _capture, data => _ = OnEntityUpdateAsync(data)))
        {
            return false;
        }

        // One write per entity, each listing the attributes to follow.
        await Gatt.WriteAsync(entityUpdate, [0, 0, 1, 2], _capture);
        await Gatt.WriteAsync(entityUpdate, [1, 0, 1, 2, 3], _capture);
        await Gatt.WriteAsync(entityUpdate, [2, 0, 1, 2, 3], _capture);
        return true;
    }

    public async Task SendAsync(byte command)
    {
        if (_remoteCommand is null)
        {
            return;
        }

        var ok = await Gatt.WriteAsync(_remoteCommand, [command], _capture);
        Log.Info("ams", $"{CommandName(command)}: {(ok ? "sent" : "failed")}");
    }

    private static void OnRemoteCommand(byte[] data) => Log.Info("ams", $"Supported commands: {string.Join(", ", data.Select(CommandName))}");

    private async Task OnEntityUpdateAsync(byte[] data)
    {
        if (data.Length < 3)
        {
            Log.Warn("ams", $"Short Entity Update value: {Convert.ToHexString(data)}");
            return;
        }

        var entity = data[0];
        var attribute = data[1];
        var truncated = (data[2] & 0x01) != 0;
        var value = Encoding.UTF8.GetString(data, 3, data.Length - 3);

        if (truncated && _entityAttribute is not null && await Gatt.WriteAsync(_entityAttribute, [entity, attribute], _capture))
        {
            var full = await Gatt.ReadAsync(_entityAttribute, _capture);

            if (full is not null)
            {
                value = Encoding.UTF8.GetString(full);
            }
        }

        var entityName = entity < _entities.Length ? _entities[entity] : $"Entity{entity}";
        var attributeName = entity < _attributes.Length && attribute < _attributes[entity].Length ? _attributes[entity][attribute] : $"Attribute{attribute}";
        Log.Info("ams", $"{entityName}.{attributeName} = \"{value}\"{(truncated ? " (was truncated)" : string.Empty)}");

        if (entity == 2 && attribute == 2)
        {
            NowPlaying = value;
        }
    }

    private static string CommandName(byte command) => command < _commands.Length ? _commands[command] : $"Command{command}";
}
