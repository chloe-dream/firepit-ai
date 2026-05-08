using Firepit.Core.Mcp;
using Firepit.Core.Projects;
using Firepit.Core.Secrets;
using Firepit.Core.Settings;

namespace Firepit.Core.Tests;

public class McpRegistryTests
{
    private static FirepitSettings WithMcp(
        Dictionary<string, McpServerSettings> servers,
        IReadOnlyList<ProjectSettings>? projects = null)
        => FirepitSettings.Defaults with { McpServers = servers, Projects = projects };

    private static ProjectContext Ctx(string path) => new(new Project("p", path, "claude-code"));

    [Fact]
    public void All_ListsAllRegisteredServers()
    {
        var settings = WithMcp(new()
        {
            ["fishbowl"] = new("Fishbowl", "http", Url: "https://localhost:7180/mcp"),
            ["grok"]     = new("Grok",     "stdio", Command: "grok-image-mcp.exe"),
        });
        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());

        var ids = registry.All.Select(e => e.Id).OrderBy(s => s).ToArray();
        Assert.Equal(["fishbowl", "grok"], ids);
    }

    [Fact]
    public void ResolveForProject_AppliesActivationFilter()
    {
        var settings = WithMcp(
            servers: new()
            {
                ["fishbowl"] = new("Fishbowl", "http", Url: "https://localhost:7180/mcp"),
                ["grok"]     = new("Grok",     "stdio", Command: "grok.exe"),
            },
            projects:
            [
                new ProjectSettings("p", @"D:\p", McpServers: ["fishbowl"]),
            ]);

        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());
        var resolved = registry.ResolveForProject(Ctx(@"D:\p"));
        var server = Assert.Single(resolved);
        Assert.Equal("fishbowl", server.Id);
        Assert.Equal("https://localhost:7180/mcp", server.Url);
    }

    [Fact]
    public void ResolveForProject_AppliesPerProjectOverrides()
    {
        var settings = WithMcp(
            servers: new()
            {
                ["fishbowl"] = new(
                    "Fishbowl", "http",
                    Url: "https://localhost:7180/mcp",
                    Headers: new Dictionary<string, string?> { ["X-Default"] = "yes" }),
            },
            projects:
            [
                new ProjectSettings(
                    "p", @"D:\p",
                    McpServers: ["fishbowl"],
                    McpOverrides: new Dictionary<string, McpOverrideSettings>
                    {
                        ["fishbowl"] = new(Headers: new Dictionary<string, string?>
                        {
                            ["X-Project"] = "p",
                        }),
                    }),
            ]);

        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());
        var server = Assert.Single(registry.ResolveForProject(Ctx(@"D:\p")));
        Assert.Equal("yes", server.Headers["X-Default"]);
        Assert.Equal("p",   server.Headers["X-Project"]);
    }

    [Fact]
    public void ResolveForProject_ReportsMissingTokensAsWarnings()
    {
        var settings = WithMcp(
            servers: new()
            {
                ["fishbowl"] = new(
                    "Fishbowl", "http",
                    Url: "https://localhost:7180/mcp",
                    Headers: new Dictionary<string, string?>
                    {
                        ["Authorization"] = "Bearer ${env:DEFINITELY_NOT_SET_12345}",
                    }),
            },
            projects:
            [
                new ProjectSettings("p", @"D:\p", McpServers: ["fishbowl"]),
            ]);

        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());
        var server = Assert.Single(registry.ResolveForProject(Ctx(@"D:\p")));
        Assert.NotEmpty(server.ResolutionWarnings);
        Assert.Contains(server.ResolutionWarnings, w => w.Contains("DEFINITELY_NOT_SET_12345"));
    }

    [Fact]
    public void ResolveForProject_OverrideNullValueRemovesKey()
    {
        var settings = WithMcp(
            servers: new()
            {
                ["fishbowl"] = new(
                    "Fishbowl", "stdio",
                    Command: "fishbowl.exe",
                    Environment: new Dictionary<string, string?> { ["LEGACY_VAR"] = "set" }),
            },
            projects:
            [
                new ProjectSettings(
                    "p", @"D:\p",
                    McpServers: ["fishbowl"],
                    McpOverrides: new Dictionary<string, McpOverrideSettings>
                    {
                        ["fishbowl"] = new(Environment: new Dictionary<string, string?>
                        {
                            ["LEGACY_VAR"] = null,
                        }),
                    }),
            ]);

        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());
        var server = Assert.Single(registry.ResolveForProject(Ctx(@"D:\p")));
        Assert.False(server.Environment.ContainsKey("LEGACY_VAR"));
    }

    [Fact]
    public void EnvironmentSecretProvider_ResolvesPresentVar()
    {
        var name = "FIREPIT_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "value-1");
        try
        {
            var resolver = new EnvironmentSecretProvider();
            Assert.True(resolver.TryResolve($"${{env:{name}}}", out var value));
            Assert.Equal("value-1", value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
