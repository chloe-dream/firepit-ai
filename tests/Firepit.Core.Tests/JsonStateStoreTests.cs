using System.IO;
using Firepit.Core.State;

namespace Firepit.Core.Tests;

public class JsonStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _statePath;

    public JsonStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "firepit-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _statePath = Path.Combine(_tempDir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignored */ }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var store = new JsonStateStore(_statePath);
        var loaded = store.Load();
        Assert.Equal(AppState.CurrentVersion, loaded.Version);
        Assert.Empty(loaded.Tabs);
    }

    [Fact]
    public void Roundtrip_PreservesTabs()
    {
        var store = new JsonStateStore(_statePath);
        var saved = new AppState(AppState.CurrentVersion,
        [
            new TabState("lighthouse", true),
            new TabState("tinderbox",  false),
        ]);
        store.Save(saved);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Tabs.Count);
        Assert.Equal("lighthouse", loaded.Tabs[0].ProjectName);
        Assert.True(loaded.Tabs[0].LastSessionResumable);
        Assert.Equal("tinderbox", loaded.Tabs[1].ProjectName);
        Assert.False(loaded.Tabs[1].LastSessionResumable);
    }

    [Fact]
    public void Load_UnknownVersion_FallsBackToEmpty()
    {
        File.WriteAllText(_statePath, """
            { "version": 99, "tabs": [{ "projectName": "x", "lastSessionResumable": true }] }
            """);
        var store = new JsonStateStore(_statePath);
        var loaded = store.Load();
        Assert.Empty(loaded.Tabs);
    }

    [Fact]
    public void Load_BadJson_FallsBackToEmpty()
    {
        File.WriteAllText(_statePath, "{ corrupted");
        var store = new JsonStateStore(_statePath);
        var loaded = store.Load();
        Assert.Empty(loaded.Tabs);
    }

    [Fact]
    public void Roundtrip_PreservesActiveTabProjectName()
    {
        var store = new JsonStateStore(_statePath);
        var saved = new AppState(
            AppState.CurrentVersion,
            [
                new TabState("lighthouse", true),
                new TabState("tinderbox",  false),
            ],
            ActiveTabProjectName: "tinderbox");
        store.Save(saved);

        var loaded = store.Load();
        Assert.Equal("tinderbox", loaded.ActiveTabProjectName);
    }

    [Fact]
    public void Roundtrip_PreservesWindowPlacement()
    {
        var store = new JsonStateStore(_statePath);
        var saved = new AppState(
            AppState.CurrentVersion,
            [],
            Window: new WindowPlacement(Left: 100.5, Top: 200, Width: 1280, Height: 720, IsMaximized: true));
        store.Save(saved);

        var loaded = store.Load();
        Assert.NotNull(loaded.Window);
        Assert.Equal(100.5, loaded.Window!.Left);
        Assert.Equal(200, loaded.Window.Top);
        Assert.Equal(1280, loaded.Window.Width);
        Assert.Equal(720, loaded.Window.Height);
        Assert.True(loaded.Window.IsMaximized);
    }

    [Fact]
    public void Load_LegacyStateWithoutWindowField_ReturnsNull()
    {
        File.WriteAllText(_statePath, """
            {
              "version": 1,
              "tabs": []
            }
            """);
        var store = new JsonStateStore(_statePath);
        var loaded = store.Load();
        Assert.Null(loaded.Window);
    }

    [Fact]
    public void Load_LegacyStateWithoutActiveField_ReturnsNull()
    {
        // Pre-v0.5.3 state.json had no activeTabProjectName field. New code
        // must tolerate the missing field and fall back to null (which the
        // restore path interprets as "no preference").
        File.WriteAllText(_statePath, """
            {
              "version": 1,
              "tabs": [
                { "projectName": "lighthouse", "lastSessionResumable": true }
              ]
            }
            """);
        var store = new JsonStateStore(_statePath);
        var loaded = store.Load();
        Assert.Null(loaded.ActiveTabProjectName);
        Assert.Single(loaded.Tabs);
    }
}
