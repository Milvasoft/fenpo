namespace Fenpo.Core.Devices;

/// <summary>
/// Maps Apple model identifiers (Device Information "Model Number", e.g. "iPhone14,5") to marketing names.
/// Unknown identifiers resolve to the generic family name.
/// </summary>
public static class ModelNames
{
    private static readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["iPhone11,2"] = "iPhone XS",
        ["iPhone11,4"] = "iPhone XS Max",
        ["iPhone11,6"] = "iPhone XS Max",
        ["iPhone11,8"] = "iPhone XR",
        ["iPhone12,1"] = "iPhone 11",
        ["iPhone12,3"] = "iPhone 11 Pro",
        ["iPhone12,5"] = "iPhone 11 Pro Max",
        ["iPhone12,8"] = "iPhone SE",
        ["iPhone13,1"] = "iPhone 12 mini",
        ["iPhone13,2"] = "iPhone 12",
        ["iPhone13,3"] = "iPhone 12 Pro",
        ["iPhone13,4"] = "iPhone 12 Pro Max",
        ["iPhone14,2"] = "iPhone 13 Pro",
        ["iPhone14,3"] = "iPhone 13 Pro Max",
        ["iPhone14,4"] = "iPhone 13 mini",
        ["iPhone14,5"] = "iPhone 13",
        ["iPhone14,6"] = "iPhone SE",
        ["iPhone14,7"] = "iPhone 14",
        ["iPhone14,8"] = "iPhone 14 Plus",
        ["iPhone15,2"] = "iPhone 14 Pro",
        ["iPhone15,3"] = "iPhone 14 Pro Max",
        ["iPhone15,4"] = "iPhone 15",
        ["iPhone15,5"] = "iPhone 15 Plus",
        ["iPhone16,1"] = "iPhone 15 Pro",
        ["iPhone16,2"] = "iPhone 15 Pro Max",
        ["iPhone17,1"] = "iPhone 16 Pro",
        ["iPhone17,2"] = "iPhone 16 Pro Max",
        ["iPhone17,3"] = "iPhone 16",
        ["iPhone17,4"] = "iPhone 16 Plus",
        ["iPhone17,5"] = "iPhone 16e",
        ["iPhone18,1"] = "iPhone 17 Pro",
        ["iPhone18,2"] = "iPhone 17 Pro Max",
        ["iPhone18,3"] = "iPhone 17",
        ["iPhone18,4"] = "iPhone Air",
    };

    /// <summary>
    /// Gets the marketing name for <paramref name="identifier"/>.
    /// </summary>
    /// <param name="identifier">Model identifier such as "iPhone14,5".</param>
    /// <returns>"iPhone 13", or "iPhone"/"iPad" for identifiers this table does not know yet.</returns>
    public static string Resolve(string identifier)
    {
        if (_names.TryGetValue(identifier.Trim(), out var name))
        {
            return name;
        }

        return identifier.StartsWith("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad" : "iPhone";
    }
}
