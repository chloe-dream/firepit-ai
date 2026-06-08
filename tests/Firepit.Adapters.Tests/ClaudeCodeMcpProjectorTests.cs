using System.IO;
using System.Text.Json.Nodes;
using Firepit.Adapters;
using Firepit.Core.Mcp;
using Firepit.Core.Projects;

namespace Firepit.Adapters.Tests;

public class ClaudeCodeMcpProjectorTests : IDisposable
{
    private readonly string _projectDir;
    private readonly ProjectContext _context;

    public ClaudeCodeMcpProjectorTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "firepit-projector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
        _context = new ProjectContext(new Project(
            Name: "test-project",
            Path: _projectDir,
            AdapterId: ClaudeCodeAdapter.AdapterId));
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* ignored */ }
    }

    private static ResolvedMcpServer Stdio(string id, string command, params string[] args)
        => new(
            Id: id,
            DisplayName: id,
            Transport: McpTransport.Stdio,
            Command: command,
            Args: args,
            Environment: new Dictionary<string, string>(),
            Url: null,
            Headers: new Dictionary<string, string>(),
            ResolutionWarnings: []);

    [Fact]
    public async Task Writes_DotMcpJson_InProjectRoot_NotUnderDotClaude()
    {
        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [Stdio("firepit", "firepit-mcp")], CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_projectDir, ".mcp.json")),
            "must write .mcp.json at the project root — Claude Code does not read .claude/mcp.json");
        Assert.False(File.Exists(Path.Combine(_projectDir, ".claude", "mcp.json")),
            "must not (re)create the stale .claude/mcp.json location");
    }

    [Fact]
    public async Task ServerNode_UsesTypeNotTransport()
    {
        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [Stdio("firepit", "firepit-mcp")], CancellationToken.None);

        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir, ".mcp.json")))!;
        var server = node["mcpServers"]!["firepit"]!;
        Assert.Equal("stdio", server["type"]!.GetValue<string>());
        Assert.Null(server["transport"]);
        Assert.Equal("firepit-mcp", server["command"]!.GetValue<string>());
    }

    [Fact]
    public async Task PreservesExistingUserAuthoredEntries()
    {
        // User has a hand-written .mcp.json with a 'ghidra' server. The
        // projector must add 'firepit' alongside, not replace the whole file.
        var existing =
            """
            {
              "mcpServers": {
                "ghidra": {
                  "type": "stdio",
                  "command": "python",
                  "args": ["bridge_mcp_ghidra.py"]
                }
              }
            }
            """;
        File.WriteAllText(Path.Combine(_projectDir, ".mcp.json"), existing);

        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [Stdio("firepit", "firepit-mcp")], CancellationToken.None);

        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir, ".mcp.json")))!;
        var servers = node["mcpServers"]!.AsObject();
        Assert.Contains("ghidra", servers.Select(kv => kv.Key));
        Assert.Contains("firepit", servers.Select(kv => kv.Key));
        Assert.Equal("python", servers["ghidra"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReplacesExistingFirepitEntry_NoDuplicate()
    {
        var existing =
            """
            {
              "mcpServers": {
                "firepit": { "type": "stdio", "command": "old-bridge.exe" }
              }
            }
            """;
        File.WriteAllText(Path.Combine(_projectDir, ".mcp.json"), existing);

        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [Stdio("firepit", "firepit-mcp")], CancellationToken.None);

        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir, ".mcp.json")))!;
        Assert.Equal("firepit-mcp", node["mcpServers"]!["firepit"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeletesLegacyDotClaudeMcpJson_OneTimeCleanup()
    {
        var legacyDir = Path.Combine(_projectDir, ".claude");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "mcp.json"),
            """{"mcpServers":{"firepit":{"transport":"stdio","command":"firepit-mcp"}}}""");

        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [Stdio("firepit", "firepit-mcp")], CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(legacyDir, "mcp.json")),
            "stale .claude/mcp.json from pre-v0.5.27 must be removed");
    }

    [Fact]
    public async Task EmptyActiveServers_AndNoExistingFile_WritesNothing()
    {
        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [], CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_projectDir, ".mcp.json")));
    }

    [Fact]
    public async Task EmptyActiveServers_PreservesUnrelatedExistingEntries()
    {
        var existing =
            """
            {
              "mcpServers": {
                "ghidra": { "type": "stdio", "command": "python" }
              }
            }
            """;
        File.WriteAllText(Path.Combine(_projectDir, ".mcp.json"), existing);

        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [], CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_projectDir, ".mcp.json")),
            "user-authored entries must survive an empty-projection pass");
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir, ".mcp.json")))!;
        Assert.Contains("ghidra", node["mcpServers"]!.AsObject().Select(kv => kv.Key));
    }

    [Fact]
    public async Task CorruptExistingFile_IsOverwrittenRatherThanCrash()
    {
        File.WriteAllText(Path.Combine(_projectDir, ".mcp.json"), "{not json");

        var projector = new ClaudeCodeMcpProjector();
        await projector.ApplyAsync(_context, [Stdio("firepit", "firepit-mcp")], CancellationToken.None);

        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir, ".mcp.json")))!;
        Assert.Equal("firepit-mcp", node["mcpServers"]!["firepit"]!["command"]!.GetValue<string>());
    }
}
