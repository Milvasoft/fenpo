using Fenpo.Core.Settings;

namespace Fenpo.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fenpo-tests", Guid.NewGuid().ToString());

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"));

        Assert.Equal(new AppSettings(), store.Current);
    }

    [Fact]
    public void UpdateIsSavedAndReloaded()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new JsonSettingsStore(path);
        AppSettings? raised = null;
        store.Changed += (_, settings) => raised = settings;

        store.Update(s => s with { Theme = AppTheme.Dark, Language = "en-US", ProximityLock = false });

        Assert.Equal(AppTheme.Dark, raised?.Theme);
        var reloaded = new JsonSettingsStore(path).Current;
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.Equal("en-US", reloaded.Language);
        Assert.False(reloaded.ProximityLock);
        Assert.Contains("\"Dark\"", File.ReadAllText(path));
    }

    [Fact]
    public void UnchangedUpdateDoesNotRaise()
    {
        var store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"));
        var raised = false;
        store.Changed += (_, _) => raised = true;

        store.Update(s => s with { });

        Assert.False(raised);
    }

    [Fact]
    public void CorruptFileGivesDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(new AppSettings(), new JsonSettingsStore(path).Current);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
