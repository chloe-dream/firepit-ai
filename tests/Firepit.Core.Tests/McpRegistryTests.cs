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

        // The registry always seeds a built-in 'firepit' entry alongside user
        // entries (v0.5.16, issue #12). User entries with id 'firepit' would
        // override; here they don't, so we expect three IDs total.
        var ids = registry.All.Select(e => e.Id).OrderBy(s => s).ToArray();
        Assert.Equal(["firepit", "fishbowl", "grok"], ids);
    }

    [Fact]
    public void All_BuiltInFirepitIsSeededWhenAbsent()
    {
        var settings = WithMcp(new());
        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());

        var firepit = registry.Find("firepit");
        Assert.NotNull(firepit);
        Assert.Equal("firepit-mcp", firepit!.Command);
        Assert.Equal(McpTransport.Stdio, firepit.Transport);
    }

    [Fact]
    public void All_UserFirepitOverridesBuiltIn()
    {
        // A user who wants to e.g. add args or env to the firepit MCP server
        // can declare their own entry under the same id. The built-in entry
        // is dropped in favour of theirs; nothing is duplicated.
        var settings = WithMcp(new()
        {
            ["firepit"] = new("My Firepit", "stdio", Command: "C:/custom/firepit-mcp.exe"),
        });
        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());

        var ids = registry.All.Select(e => e.Id).OrderBy(s => s).ToArray();
        Assert.Equal(["firepit"], ids);
        Assert.Equal("C:/custom/firepit-mcp.exe", registry.Find("firepit")!.Command);
    }

    // v0.5.20: the built-in firepit MCP server is now implicit in
    // ResolveForProject too — Inbox button / firepit_send_to / firepit_*
    // tools work out of the box in every project. Filter it out in tests
    // that only care about user-declared servers.
    private static ResolvedMcpServer[] NonFirepit(IEnumerable<ResolvedMcpServer> resolved)
        => resolved.Where(s => s.Id != "firepit").ToArray();

    [Fact]
    public void ResolveForProject_UnknownActivationViaProjectConfigCallsWarn()
    {
        // The new path that goes via .firepit/config.json mcpActivations — when
        // it references an id that's not in the global registry (or built-in),
        // the registry now logs through the warn callback instead of silently
        // dropping. v0.5.16 fix for issue #12: pre-v0.5.16 this drop is exactly
        // why "Projecting 0 MCP server(s)" went unnoticed.
        var warnings = new List<string>();
        var settings = WithMcp(new());
        var registry = new SettingsBackedMcpRegistry(
            settings,
            new EnvironmentSecretProvider(),
            projectConfigLoader: _ => new Firepit.Core.ProjectConfig.ProjectConfig(
                Version: 1,
                McpActivations:
                [
                    new Firepit.Core.ProjectConfig.ProjectMcpActivation(Id: "nope-not-registered"),
                ]),
            warn: msg => warnings.Add(msg));

        var resolved = registry.ResolveForProject(Ctx(@"D:\p"));
        // Unknown activation dropped + warned; built-in firepit still projected.
        var nonFirepit = NonFirepit(resolved);
        Assert.Empty(nonFirepit);
        Assert.Contains(warnings, w => w.Contains("nope-not-registered"));
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
        var server = Assert.Single(NonFirepit(resolved));
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
        var server = Assert.Single(NonFirepit(registry.ResolveForProject(Ctx(@"D:\p"))));
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
        var server = Assert.Single(NonFirepit(registry.ResolveForProject(Ctx(@"D:\p"))));
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
        var server = Assert.Single(NonFirepit(registry.ResolveForProject(Ctx(@"D:\p"))));
        Assert.False(server.Environment.ContainsKey("LEGACY_VAR"));
    }

    [Fact]
    public void ResolveForProject_AlwaysIncludesFirepitBuiltIn()
    {
        // Even with no activations at all, the built-in firepit server is
        // projected so toolbar features like the Inbox button work out of
        // the box (v0.5.20).
        var settings = WithMcp(new());
        var registry = new SettingsBackedMcpRegistry(settings, new EnvironmentSecretProvider());
        var resolved = registry.ResolveForProject(Ctx(@"D:\p"));
        var firepit = Assert.Single(resolved);
        Assert.Equal("firepit", firepit.Id);
    }

    [Fact]
    public void ResolveForProject_ExplicitFirepitActivationIsNotDuplicated()
    {
        // A user who lists { "id": "firepit" } in mcpActivations (e.g. to
        // pass envOverrides) wins — the implicit add is suppressed so the
        // server appears only once.
        var settings = WithMcp(new());
        var registry = new SettingsBackedMcpRegistry(
            settings,
            new EnvironmentSecretProvider(),
            projectConfigLoader: _ => new Firepit.Core.ProjectConfig.ProjectConfig(
                Version: 1,
                McpActivations:
                [
                    new Firepit.Core.ProjectConfig.ProjectMcpActivation(
                        Id: "firepit",
                        EnvOverrides: new Dictionary<string, string?> { ["MY_ENV"] = "set" }),
                ]));
        var resolved = registry.ResolveForProject(Ctx(@"D:\p"));
        var firepit = Assert.Single(resolved);
        Assert.Equal("firepit", firepit.Id);
        Assert.Equal("set", firepit.Environment["MY_ENV"]);
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
