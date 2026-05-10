using System.IO;
using Firepit.Core.ProjectConfig;
using Firepit.Core.Settings;

namespace Firepit.Core.Tests.ProjectConfig;

public class JsonProjectConfigStoreTests : IDisposable
{
    private readonly string _projectDir;
    private readonly JsonProjectConfigStore _store = new();

    public JsonProjectConfigStoreTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "firepit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Load_ReturnsNullWhenFileMissing()
    {
        Assert.Null(_store.Load(_projectDir));
    }

    [Fact]
    public void RoundTrip_PreservesAllSections()
    {
        var original = new Firepit.Core.ProjectConfig.ProjectConfig(
            Version: 1,
            Id: "lighthouse",
            QuickLinks:
            [
                new ProjectQuickLink("GitHub", "https://github.com/me/lighthouse",
                    QuickLinkTargetSetting.External, Icon: "github"),
            ],
            McpActivations:
            [
                new ProjectMcpActivation(
                    Id: "fishbowl",
                    HeaderOverrides: new Dictionary<string, string?>
                    {
                        ["Authorization"] = "Bearer ${cred:firepit/fishbowl-lighthouse}",
                    }),
            ],
            Agent: new ProjectAgentConfig(
                Command: "claude",
                Args: ["--continue"],
                EnvOverrides: new Dictionary<string, string?> { ["FOO"] = "bar" }),
            Session: new ProjectSessionConfig(
                EnvOverrides: new Dictionary<string, string?> { ["PROJECT_TAG"] = "v2" }));

        _store.Save(_projectDir, original);
        var loaded = _store.Load(_projectDir);

        Assert.NotNull(loaded);
        Assert.Equal("lighthouse", loaded!.Id);
        Assert.Single(loaded.QuickLinks!);
        Assert.Equal("GitHub", loaded.QuickLinks![0].Name);
        Assert.Single(loaded.McpActivations!);
        Assert.Equal("fishbowl", loaded.McpActivations![0].Id);
        Assert.Equal("Bearer ${cred:firepit/fishbowl-lighthouse}",
            loaded.McpActivations![0].HeaderOverrides!["Authorization"]);
        Assert.Equal("claude", loaded.Agent!.Command);
        Assert.Equal("v2", loaded.Session!.EnvOverrides!["PROJECT_TAG"]);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_projectDir, "nested", "project");
        Directory.CreateDirectory(nested);

        _store.Save(nested, new Firepit.Core.ProjectConfig.ProjectConfig());
        Assert.True(File.Exists(Path.Combine(nested, ".firepit", "config.json")));
    }

    [Fact]
    public void Load_ReturnsNullOnMalformedJson()
    {
        var dir = Path.Combine(_projectDir, ".firepit");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "config.json"), "{ this is not valid json");

        Assert.Null(_store.Load(_projectDir));
    }

    [Fact]
    public void ResolvePath_PointsAtFirepitConfigJson()
    {
        var path = JsonProjectConfigStore.ResolvePath(_projectDir);
        Assert.Equal(Path.Combine(_projectDir, ".firepit", "config.json"), path);
    }

    [Fact]
    public void RoundTrip_CommandsAllThreeTypes()
    {
        var original = new Firepit.Core.ProjectConfig.ProjectConfig(
            Commands:
            [
                new ProjectCommand("Tests", ProjectCommandType.Shell,
                    Icon: "play", Command: "npm", Args: ["test"]),
                new ProjectCommand("Deploy", ProjectCommandType.ClaudePrompt,
                    Icon: "rocket", Prompt: "Deploy to staging"),
                new ProjectCommand("Docs", ProjectCommandType.Url,
                    Icon: "book", Url: "https://example.com/docs"),
            ]);

        _store.Save(_projectDir, original);
        var loaded = _store.Load(_projectDir);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Commands!.Count);
        Assert.Equal(ProjectCommandType.Shell,        loaded.Commands![0].Type);
        Assert.Equal("npm",                            loaded.Commands![0].Command);
        Assert.Equal("test",                           loaded.Commands![0].Args![0]);
        Assert.Equal(ProjectCommandType.ClaudePrompt, loaded.Commands![1].Type);
        Assert.Equal("Deploy to staging",              loaded.Commands![1].Prompt);
        Assert.Equal(ProjectCommandType.Url,          loaded.Commands![2].Type);
        Assert.Equal("https://example.com/docs",       loaded.Commands![2].Url);
    }
}
