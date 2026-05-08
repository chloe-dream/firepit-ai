using System.IO;
using Firepit.Core.Settings;

namespace Firepit.Core.Tests;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public JsonSettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "firepit-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignored */ }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(_settingsPath);
        var loaded = store.Load();
        Assert.Equal(FirepitSettings.Defaults.DefaultAgent, loaded.DefaultAgent);
        Assert.Equal(FirepitSettings.Defaults.Theme, loaded.Theme);
    }

    [Fact]
    public void SaveLoadRoundtrip_PreservesValues()
    {
        var store = new JsonSettingsStore(_settingsPath);
        var written = FirepitSettings.Defaults with
        {
            ProjectsRoot = @"D:\Code\Projects",
            QuickLinks =
            [
                new QuickLinkSettings("GitHub", "https://github.com/foo/{projectName}"),
            ],
        };
        store.Save(written);

        var read = store.Load();
        Assert.Equal(@"D:\Code\Projects", read.ProjectsRoot);
        Assert.NotNull(read.QuickLinks);
        Assert.Single(read.QuickLinks!);
        Assert.Equal("GitHub", read.QuickLinks![0].Name);
    }

    [Fact]
    public void Load_BadJson_FallsBackToDefaults()
    {
        File.WriteAllText(_settingsPath, "{ this is not json");
        var store = new JsonSettingsStore(_settingsPath);
        var loaded = store.Load();
        Assert.Equal(FirepitSettings.Defaults.DefaultAgent, loaded.DefaultAgent);
    }

    [Fact]
    public void Save_CreatesParentDirectory()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "dir", "settings.json");
        var store = new JsonSettingsStore(nestedPath);
        store.Save(FirepitSettings.Defaults);
        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void JsoncWithComments_IsAccepted()
    {
        File.WriteAllText(_settingsPath, """
            {
              // user comment
              "projectsRoot": "D:\\Code\\Foo",
              "defaultAgent": "claude",
              "theme": "dark",
              "tabs": { "persistAcrossRestarts": true, "activityIdleThresholdMs": 2000 },
              "shells": { "preferred": "wt" }
            }
            """);
        var store = new JsonSettingsStore(_settingsPath);
        var loaded = store.Load();
        Assert.Equal(@"D:\Code\Foo", loaded.ProjectsRoot);
        Assert.Equal(2000, loaded.Tabs.ActivityIdleThresholdMs);
    }
}
