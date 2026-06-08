using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Firepit.Core.Mcp;
using Firepit.Core.Projects;

namespace Firepit.Adapters;

/// <summary>
/// Projects Firepit's MCP registry into Claude Code's expected format by writing
/// a per-project <c>.mcp.json</c> file in the project root — the only project-scope
/// location Claude Code reads. Earlier Firepit versions wrote <c>.claude/mcp.json</c>,
/// which Claude Code silently ignored; that stale file is best-effort deleted here.
/// User-authored entries in the same <c>.mcp.json</c> are preserved by merging by
/// server id rather than overwriting the whole file.
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

        var target = Path.Combine(context.Path, ".mcp.json");

        var (root, servers) = LoadOrCreate(target);

        // Replace every server we manage by id; entries we don't manage (user-
        // authored, other tools) are left untouched.
        foreach (var server in activeServers)
        {
            servers[server.Id] = BuildServerNode(server);
        }

        if (servers.Count == 0)
        {
            // Nothing to project AND no existing entries → don't litter empty files.
            try { File.Delete(target); } catch (IOException) { /* ignored */ }
        }
        else
        {
            File.WriteAllText(target, root.ToJsonString(JsonOptions));
        }

        // One-time cleanup: pre-v0.5.27 builds wrote .claude/mcp.json, which
        // Claude Code never reads. Tolerate any error here — it's not load-bearing.
        var legacy = Path.Combine(context.Path, ".claude", "mcp.json");
        try { if (File.Exists(legacy)) File.Delete(legacy); } catch (IOException) { /* ignored */ }

        return Task.CompletedTask;
    }

    private static (JsonObject root, JsonObject servers) LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var parsed = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                if (parsed is not null)
                {
                    if (parsed["mcpServers"] is JsonObject existing)
                    {
                        return (parsed, existing);
                    }
                    var fresh = new JsonObject();
                    parsed["mcpServers"] = fresh;
                    return (parsed, fresh);
                }
            }
            catch (JsonException)
            {
                // Existing file is corrupt — overwrite rather than crash. User
                // can recover from git if they hand-authored entries.
            }
            catch (IOException) { /* fall through to fresh */ }
        }
        var servers = new JsonObject();
        var root = new JsonObject { ["mcpServers"] = servers };
        return (root, servers);
    }

    private static JsonObject BuildServerNode(ResolvedMcpServer server)
    {
        var node = new JsonObject();
        // Claude Code's schema uses 'type'; 'transport' is silently ignored
        // (which is what caused firepit_* tools to never appear pre-v0.5.27).
        node["type"] = TransportLabel(server.Transport);

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
