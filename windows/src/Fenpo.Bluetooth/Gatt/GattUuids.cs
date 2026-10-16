namespace Fenpo.Bluetooth.Gatt;

/// <summary>
/// UUIDs of the GATT services Fenpo uses on the phone.
/// </summary>
public static class GattUuids
{
    /// <summary>Apple Notification Center Service.</summary>
    public static readonly Guid Ancs = new("7905F431-B5CE-4E99-A40F-4B1E122D00D0");

    /// <summary>ANCS Notification Source.</summary>
    public static readonly Guid AncsNotificationSource = new("9FBF120D-6301-42D9-8C58-25E699A21DBD");

    /// <summary>ANCS Control Point.</summary>
    public static readonly Guid AncsControlPoint = new("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");

    /// <summary>ANCS Data Source.</summary>
    public static readonly Guid AncsDataSource = new("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");

    /// <summary>Apple Media Service.</summary>
    public static readonly Guid Ams = new("89D3502B-0F36-433A-8EF4-C502AD55F8DC");

    /// <summary>Apple Continuity, offered by every iPhone (ANCS hides itself while notification sharing is off).</summary>
    public static readonly Guid AppleContinuity = new("D0611E78-BBB4-4591-A5F8-487910AE4366");

    /// <summary>AMS Remote Command.</summary>
    public static readonly Guid AmsRemoteCommand = new("9B3C81D8-57B1-4A8A-B8DF-0E56F7CA51C2");

    /// <summary>AMS Entity Update.</summary>
    public static readonly Guid AmsEntityUpdate = new("2F7CABCE-808D-411F-9A0C-BB92BA96C102");

    /// <summary>AMS Entity Attribute.</summary>
    public static readonly Guid AmsEntityAttribute = new("C6B2F38C-23AB-46D8-A6AB-A3A870BBD5D7");

    /// <summary>Battery Service.</summary>
    public static readonly Guid Battery = Short(0x180F);

    /// <summary>Battery Level.</summary>
    public static readonly Guid BatteryLevel = Short(0x2A19);

    /// <summary>Current Time Service.</summary>
    public static readonly Guid CurrentTime = Short(0x1805);

    /// <summary>Current Time characteristic.</summary>
    public static readonly Guid CurrentTimeValue = Short(0x2A2B);

    /// <summary>Device Information Service.</summary>
    public static readonly Guid DeviceInformation = Short(0x180A);

    /// <summary>Model Number String.</summary>
    public static readonly Guid ModelNumber = Short(0x2A24);

    /// <summary>
    /// Expands a 16-bit Bluetooth SIG UUID.
    /// </summary>
    /// <param name="shortId">16-bit id.</param>
    /// <returns>The full UUID.</returns>
    public static Guid Short(ushort shortId) => new($"0000{shortId:X4}-0000-1000-8000-00805F9B34FB");
}
