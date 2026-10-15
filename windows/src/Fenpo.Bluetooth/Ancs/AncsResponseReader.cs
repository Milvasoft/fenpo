using System.Text;

namespace Fenpo.Bluetooth.Ancs;

/// <summary>
/// A complete Data Source response: notification uid or app id, and attribute values by id.
/// </summary>
/// <param name="CommandId">0 for notification attributes, 1 for app attributes.</param>
/// <param name="Uid">Notification uid (notification attributes only).</param>
/// <param name="AppIdentifier">Bundle id (app attributes only).</param>
/// <param name="Values">Attribute values as UTF-8 text.</param>
public sealed record AncsResponse(byte CommandId, uint Uid, string? AppIdentifier, IReadOnlyDictionary<byte, string> Values)
{
    /// <summary>
    /// Gets an attribute value or an empty string.
    /// </summary>
    /// <param name="attribute">Attribute id.</param>
    /// <returns>The value.</returns>
    public string this[AncsAttribute attribute] => Values.TryGetValue((byte)attribute, out var value) ? value : string.Empty;
}

/// <summary>
/// Reassembles Data Source notifications into one response. The phone splits responses at the MTU (S1 never saw a split
/// at MTU 527, but smaller MTUs do), so completeness is decided by the length fields of the attributes asked for.
/// </summary>
public sealed class AncsResponseReader
{
    private readonly List<byte> _buffer = [];
    private byte _commandId;
    private int _attributeCount;
    private bool _expecting;

    /// <summary>
    /// Gets a value indicating whether a response is expected.
    /// </summary>
    public bool Expecting => _expecting;

    /// <summary>
    /// Starts waiting for the response to a command; drops anything buffered.
    /// </summary>
    /// <param name="commandId">Command id sent.</param>
    /// <param name="attributeCount">Number of attributes asked for.</param>
    public void Expect(byte commandId, int attributeCount)
    {
        _buffer.Clear();
        _commandId = commandId;
        _attributeCount = attributeCount;
        _expecting = true;
    }

    /// <summary>
    /// Stops waiting and drops anything buffered.
    /// </summary>
    public void Reset()
    {
        _buffer.Clear();
        _expecting = false;
    }

    /// <summary>
    /// Adds a Data Source fragment.
    /// </summary>
    /// <param name="fragment">Characteristic value.</param>
    /// <param name="response">The response when it is complete.</param>
    /// <returns>True when <paramref name="response"/> is complete.</returns>
    public bool Add(ReadOnlySpan<byte> fragment, out AncsResponse? response)
    {
        response = null;

        if (!_expecting)
        {
            return false;
        }

        _buffer.AddRange(fragment.ToArray());

        if (!TryParse([.. _buffer], out response))
        {
            return false;
        }

        _buffer.Clear();
        _expecting = false;
        return true;
    }

    private bool TryParse(byte[] buffer, out AncsResponse? response)
    {
        response = null;

        if (buffer.Length < 1 || buffer[0] != _commandId)
        {
            return false;
        }

        int position;
        uint uid = 0;
        string? appIdentifier = null;

        if (_commandId == AncsCommands.GetNotificationAttributesId)
        {
            if (buffer.Length < 5)
            {
                return false;
            }

            uid = BitConverter.ToUInt32(buffer, 1);
            position = 5;
        }
        else
        {
            var terminator = Array.IndexOf(buffer, (byte)0, 1);

            if (terminator < 0)
            {
                return false;
            }

            appIdentifier = Encoding.UTF8.GetString(buffer, 1, terminator - 1);
            position = terminator + 1;
        }

        var values = new Dictionary<byte, string>();

        for (var i = 0; i < _attributeCount; i++)
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

            values[id] = Encoding.UTF8.GetString(buffer, position + 3, length);
            position += 3 + length;
        }

        response = new AncsResponse(_commandId, uid, appIdentifier, values);
        return true;
    }
}
