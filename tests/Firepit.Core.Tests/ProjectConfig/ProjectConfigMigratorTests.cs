using System.IO;
using Firepit.Core.ProjectConfig;
using Firepit.Core.Settings;

namespace Firepit.Core.Tests.ProjectConfig;

public class ProjectConfigMigratorTests : IDisposable
{
    private readonly string _root;
    private readonly JsonProjectConfigStore _store = new();

    public ProjectConfigMigratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "firepit-migrator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Migrate_NoProjects_ReturnsZero()
    {
        var settings = MakeSettings(projects: []);
        var result = new ProjectConfigMigrator(_store).Migrate(settings);

        Assert.Equal(0, result.MigratedCount);
        Assert.Same(settings, result.Settings);
    }

    [Fact]
    public void Migrate_ProjectWithoutMigratableFields_IsSkipped()
    {
        var dir = MakeProjectDir("plain");
        var settings = MakeSettings(projects: [new ProjectSettings(Name: "plain", Path: dir)]);

        var result = new ProjectConfigMigrator(_store).Migrate(settings);

        Assert.Equal(0, result.MigratedCount);
        Assert.Null(_store.Load(dir));
    }

    [Fact]
    public void Migrate_QuickLinks_MovesToProjectFile()
    {
        var dir = MakeProjectDir("lighthouse");
        var settings = MakeSettings(projects:
        [
            new ProjectSettings(
                Name: "lighthouse",
                Path: dir,
                QuickLinks:
                [
                    new QuickLinkSettings("GitHub", "https://github.com/me/lighthouse",
                        QuickLinkTargetSetting.External, Icon: "github"),
                ]),
        ]);

        var result = new ProjectConfigMigrator(_store).Migrate(settings);

        Assert.Equal(1, result.MigratedCount);
        var loaded = _store.Load(dir);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.QuickLinks!);
        Assert.Equal("GitHub", loaded.QuickLinks![0].Name);

        // Global entry stripped of QuickLinks but Path/Name preserved
        var globalEntry = result.Settings.Projects!.Single();
        Assert.Equal("lighthouse", globalEntry.Name);
        Assert.Equal(dir, globalEntry.Path);
        Assert.Null(globalEntry.QuickLinks);
    }

    [Fact]
    public void Migrate_McpActivationsAndOverrides_PreserveHeaders()
    {
        var dir = MakeProjectDir("with-fishbowl");
        var settings = MakeSettings(projects:
        [
            new ProjectSettings(
                Name: "with-fishbowl",
                Path: dir,
                McpServers: ["fishbowl"],
                McpOverrides: new Dictionary<string, McpOverrideSettings>
                {
                    ["fishbowl"] = new McpOverrideSettings(
                        Headers: new Dictionary<string, string?>
                        {
                            ["Authorization"] = "Bearer ${cred:firepit/fishbowl-with-fishbowl}",
                        }),
                }),
        ]);

        var result = new ProjectConfigMigrator(_store).Migrate(settings);

        Assert.Equal(1, result.MigratedCount);
        var loaded = _store.Load(dir);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.McpActivations!);
        Assert.Equal("fishbowl", loaded.McpActivations![0].Id);
        Assert.Equal("Bearer ${cred:firepit/fishbowl-with-fishbowl}",
            loaded.McpActivations![0].HeaderOverrides!["Authorization"]);

        var globalEntry = result.Settings.Projects!.Single();
        Assert.Null(globalEntry.McpServers);
        Assert.Null(globalEntry.McpOverrides);
    }

    [Fact]
    public void Migrate_AgentOverride_MovesToAgentSection()
    {
        var dir = MakeProjectDir("custom-agent");
        var settings = MakeSettings(projects:
        [
            new ProjectSettings(
                Name: "custom-agent",
                Path: dir,
                AgentCommand: "claude-stable",
                AgentArgs: ["--no-update"]),
        ]);

        var result = new ProjectConfigMigrator(_store).Migrate(settings);

        Assert.Equal(1, result.MigratedCount);
        var loaded = _store.Load(dir);
        Assert.NotNull(loaded);
        Assert.Equal("claude-stable", loaded!.Agent!.Command);
        Assert.Equal(["--no-update"], loaded.Agent.Args);

        var globalEntry = result.Settings.Projects!.Single();
        Assert.Null(globalEntry.AgentCommand);
        Assert.Null(globalEntry.AgentArgs);
    }

    [Fact]
    public void Migrate_DoesNotClobberExistingProjectFile()
    {
        var dir = MakeProjectDir("preserved");
        // Pre-existing project config that the user already curated
        var pre = new Firepit.Core.ProjectConfig.ProjectConfig(
            QuickLinks: [new ProjectQuickLink("Custom", "https://example.com")]);
        _store.Save(dir, pre);

        var settings = MakeSettings(projects:
        [
            new ProjectSettings(
                Name: "preserved",
                Path: dir,
                QuickLinks: [new QuickLinkSettings("FromGlobal", "https://global.com")]),
        ]);

        var result = new ProjectConfigMigrator(_store).Migrate(settings);

        // Migration counts the project (settings stripped) but the existing
        // file is left alone — user's curated config wins.
        Assert.Equal(1, result.MigratedCount);
        var loaded = _store.Load(dir);
        Assert.Equal("Custom", loaded!.QuickLinks![0].Name);
        Assert.Null(result.Settings.Projects!.Single().QuickLinks);
    }

    [Fact]
    public void Migrate_NonexistentProjectPath_IsSkipped()
    {
        var ghost = Path.Combine(_root, "does-not-exist");
        var settings = MakeSettings(projects:
        [
            new ProjectSettings(
                Name: "ghost",
                Path: ghost,
                QuickLinks: [new QuickLinkSettings("X", "https://x.com")]),
        ]);

        var result = new ProjectConfigMigrator(_store).Migrate(settings);

        Assert.Equal(0, result.MigratedCount);
        // Original entry preserved untouched
        Assert.NotNull(result.Settings.Projects!.Single().QuickLinks);
    }

    private string MakeProjectDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static FirepitSettings MakeSettings(IReadOnlyList<ProjectSettings> projects) =>
        FirepitSettings.Defaults with { Projects = projects };
}
