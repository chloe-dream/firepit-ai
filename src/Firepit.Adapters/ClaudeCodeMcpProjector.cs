using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Firepit.Core.Mcp;
using Firepit.Core.Projects;

namespace Firepit.Adapters;

/// <summary>
/// Projects Firepit's MCP registry into Claude Code's expected format by writing
/// a per-project <c>.claude/mcp.json</c> file in the project's working directory.
/// Implementation choice from ARCHITECTURE §9.3 — file-based, no extra invocations.
/// </summary>
public sealed class ClaudeCodeMcpProjector : IAgentMcpProjector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public Task ApplyAsync(
        ProjectContext context,
        IReadOnlyList<ResolvedMcpServer> activeServers,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(activeServers);
        ct.ThrowIfCancellationRequested();

        var claudeDir = Path.Combine(context.Path, ".claude");
        Directory.CreateDirectory(claudeDir);
        var target = Path.Combine(claudeDir, "mcp.json");

        if (activeServers.Count == 0)
        {
            // Remove the file so a previously-active project that's now empty stops
            // declaring stale servers. Tolerate missing file.
            try { File.Delete(target); } catch (IOException) { /* ignored */ }
            return Task.CompletedTask;
        }

        var servers = new JsonObject();
        foreach (var server in activeServers)
        {
            servers[server.Id] = BuildServerNode(server);
        }
        var root = new JsonObject { ["mcpServers"] = servers };
        File.WriteAllText(target, root.ToJsonString(JsonOptions));
        return Task.CompletedTask;
    }

    private static JsonObject BuildServerNode(ResolvedMcpServer server)
    {
        var node = new JsonObject();
        node["transport"] = TransportLabel(server.Transport);

        switch (server.Transport)
        {
            case McpTransport.Stdio:
                if (server.Command is not null) node["command"] = server.Command;
                if (server.Args.Count > 0)       node["args"]    = ToArray(server.Args);
                if (server.Environment.Count > 0) node["env"]    = ToObject(server.Environment);
                break;
            case McpTransport.Http:
            case McpTransport.Sse:
                if (server.Url is not null)       node["url"]     = server.Url;
                if (server.Headers.Count > 0)     node["headers"] = ToObject(server.Headers);
                break;
        }
        return node;
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var v in values)
        {
            array.Add(v);
        }
        return array;
    }

    private static JsonObject ToObject(IReadOnlyDictionary<string, string> values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values)
        {
            obj[key] = value;
        }
        return obj;
    }

    private static string TransportLabel(McpTransport transport) => transport switch
    {
        McpTransport.Stdio => "stdio",
        McpTransport.Http  => "http",
        McpTransport.Sse   => "sse",
        _                  => "stdio",
    };
}
