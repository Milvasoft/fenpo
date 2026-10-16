using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fenpo.Core.Settings;

/// <summary>
/// Settings store backed by a JSON file. A missing or unreadable file yields the defaults.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Lock _lock = new();
    private AppSettings _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSettingsStore"/> class and loads <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Full path of the settings file.</param>
    public JsonSettingsStore(string path)
    {
        _path = path;
        _current = Load(path);
    }

    /// <inheritdoc/>
    public event EventHandler<AppSettings>? Changed;

    /// <inheritdoc/>
    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc/>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated;

        lock (_lock)
        {
            updated = change(_current);

            if (updated == _current)
            {
                return;
            }

            _current = updated;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(updated, _options));
            File.Move(temporary, _path, overwrite: true);
        }

        Changed?.Invoke(this, updated);
    }

    private static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), _options) ?? new AppSettings() : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
