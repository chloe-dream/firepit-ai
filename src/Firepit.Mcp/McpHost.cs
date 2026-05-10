using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Serilog;

namespace Firepit.Mcp;

/// <summary>
/// In-process named-pipe MCP server. firepit-mcp.exe (the stdio bridge)
/// connects here as a pure tunnel; the wire format on the pipe is the same
/// JSON-RPC envelopes Claude Code sends, framed with a 4-byte little-endian
/// length prefix.
/// </summary>
public sealed class McpHost : IDisposable
{
    public const string PipeName    = "firepit-mcp";
    public const int    MaxClients  = 8;
    public const string Protocol    = "2024-11-05";
    public const string ServerName  = "firepit";

    private readonly IMcpBackend _backend;
    private readonly string      _serverVersion;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public McpHost(IMcpBackend backend, string serverVersion = "0.5.0")
    {
        _backend       = backend ?? throw new ArgumentNullException(nameof(backend));
        _serverVersion = serverVersion;
    }

    public void Start()
    {
        for (var i = 0; i < MaxClients; i++)
        {
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
        Log.Information("MCP host listening on pipe '{Pipe}' ({Max} concurrent)", PipeName, MaxClients);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { /* ignored */ }
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, MaxClients,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(ct);
                Log.Debug("MCP client connected");
                await HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException ex)
            {
                Log.Debug(ex, "MCP pipe IO ended (client disconnect or shutdown)");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MCP pipe accept-loop error");
            }
            finally
            {
                try { pipe?.Dispose(); } catch { /* ignored */ }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            if (!await ReadExactlyAsync(pipe, lengthBuf, 0, 4, ct)) return;
            var length = BitConverter.ToInt32(lengthBuf, 0);
            if (length <= 0 || length > 16 * 1024 * 1024) return;
            var payload = new byte[length];
            if (!await ReadExactlyAsync(pipe, payload, 0, length, ct)) return;

            string? responseJson;
            try
            {
                responseJson = await DispatchAsync(payload);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MCP dispatch error");
                responseJson = BuildErrorResponse(JsonValue.Create((string?)null), -32603, "Internal error: " + ex.Message);
            }

            if (responseJson is null) continue; // notification — no response
            var resp = Encoding.UTF8.GetBytes(responseJson);
            var lb = BitConverter.GetBytes(resp.Length);
            try
            {
                await pipe.WriteAsync(lb, ct);
                await pipe.WriteAsync(resp, ct);
                await pipe.FlushAsync(ct);
            }
            catch (IOException) { return; }
        }
    }

    /// <summary>
    /// Returns the response JSON string, or null for notifications (no response).
    /// </summary>
    private async Task<string?> DispatchAsync(byte[] payload)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException ex)
        {
            return BuildErrorResponse(null, -32700, "Parse error: " + ex.Message);
        }
        if (root is null) return null;

        var idNode = root["id"];
        var method = root["method"]?.GetValue<string>();
        var paramsNode = root["params"];

        if (string.IsNullOrEmpty(method))
        {
            return BuildErrorResponse(idNode, -32600, "Invalid request: missing method");
        }

        // Notifications have no id; they expect no response.
        var isNotification = idNode is null;

        try
        {
            return method switch
            {
                "initialize"                 => BuildResult(idNode, BuildInitializeResult()),
                "notifications/initialized"  => null,
                "tools/list"                 => BuildResult(idNode, BuildToolsList()),
                "tools/call"                 => await HandleToolsCallAsync(idNode, paramsNode),
                "resources/list"             => BuildResult(idNode, BuildResourcesList()),
                "resources/read"             => await HandleResourcesReadAsync(idNode, paramsNode),
                "ping"                       => BuildResult(idNode, new JsonObject()),
                _ when isNotification        => null,
                _                            => BuildErrorResponse(idNode, -32601, $"Method not found: {method}"),
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Dispatch handler threw for {Method}", method);
            return BuildErrorResponse(idNode, -32603, "Internal error: " + ex.Message);
        }
    }

    // --- initialize -----------------------------------------------------

    private JsonObject BuildInitializeResult() => new()
    {
        ["protocolVersion"] = Protocol,
        ["capabilities"] = new JsonObject
        {
            ["tools"]     = new JsonObject(),
            ["resources"] = new JsonObject(),
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"]    = ServerName,
            ["version"] = _serverVersion,
        },
    };

    // --- tools/list -----------------------------------------------------

    private JsonObject BuildToolsList()
    {
        var tools = new JsonArray();
        foreach (var def in McpToolDefinitions.All)
        {
            tools.Add(new JsonObject
            {
                ["name"]        = def.Name,
                ["description"] = def.Description,
                ["inputSchema"] = JsonNode.Parse(def.InputSchemaJson)!,
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    // --- tools/call -----------------------------------------------------

    private async Task<string> HandleToolsCallAsync(JsonNode? id, JsonNode? paramsNode)
    {
        var name = paramsNode?["name"]?.GetValue<string>();
        var args = paramsNode?["arguments"] as JsonObject ?? new JsonObject();

        Log.Information("MCP tools/call: {Name}", name);

        switch (name)
        {
            case "firepit_list_projects":
            {
                var projects = await _backend.ListProjectsAsync();
                return BuildResult(id, BuildContentJson(JsonSerializer.Serialize(projects, McpJsonContext.Default.IReadOnlyListProjectInfo)));
            }
            case "firepit_open_tab":
            {
                var project = args["projectName"]?.GetValue<string>();
                var resume  = args["resume"]?.GetValue<bool?>() ?? false;
                if (string.IsNullOrEmpty(project))
                    return BuildErrorResponse(id, -32602, "missing 'projectName'");
                var result = await _backend.OpenTabAsync(project, resume);
                return BuildResult(id, BuildContentJson(JsonSerializer.Serialize(result, McpJsonContext.Default.ToolCallResult)));
            }
            case "firepit_focus_tab":
            {
                var project = args["projectName"]?.GetValue<string>();
                if (string.IsNullOrEmpty(project))
                    return BuildErrorResponse(id, -32602, "missing 'projectName'");
                var result = await _backend.FocusTabAsync(project);
                return BuildResult(id, BuildContentJson(JsonSerializer.Serialize(result, McpJsonContext.Default.ToolCallResult)));
            }
            case "firepit_close_tab":
            {
                var project = args["projectName"]?.GetValue<string>();
                if (string.IsNullOrEmpty(project))
                    return BuildErrorResponse(id, -32602, "missing 'projectName'");
                var result = await _backend.CloseTabAsync(project);
                return BuildResult(id, BuildContentJson(JsonSerializer.Serialize(result, McpJsonContext.Default.ToolCallResult)));
            }
            case "firepit_reload":
            {
                var project = args["projectName"]?.GetValue<string>();
                var restart = args["restart"]?.GetValue<bool?>() ?? false;
                if (string.IsNullOrEmpty(project))
                    return BuildErrorResponse(id, -32602, "missing 'projectName'");
                var result = await _backend.ReloadAsync(project, restart);
                return BuildResult(id, BuildContentJson(JsonSerializer.Serialize(result, McpJsonContext.Default.ToolCallResult)));
            }
            case "firepit_send_to":
            {
                var fromProject = ReadEnvProjectName();
                var toProject   = args["toProject"]?.GetValue<string>();
                var subject     = args["subject"]?.GetValue<string>();
                var body        = args["body"]?.GetValue<string>();
                var priority    = args["priority"]?.GetValue<string?>() ?? "normal";
                if (string.IsNullOrEmpty(toProject) || string.IsNullOrEmpty(subject) || body is null)
                    return BuildErrorResponse(id, -32602, "missing 'toProject', 'subject', or 'body'");
                if (string.IsNullOrEmpty(fromProject))
                    return BuildErrorResponse(id, -32603, "FIREPIT_PROJECT_NAME not set on caller — cannot determine sender");
                var result = await _backend.SendInboxAsync(fromProject, toProject, subject, body, priority);
                return BuildResult(id, BuildContentJson(JsonSerializer.Serialize(result, McpJsonContext.Default.InboxWriteResult)));
            }
            default:
                return BuildErrorResponse(id, -32601, $"Unknown tool: {name}");
        }
    }

    /// <summary>
    /// Reads FIREPIT_PROJECT_NAME from the bridge process's environment via
    /// the env that Claude Code passed through. Currently we don't have a
    /// way to read remote env, so we expect the bridge to embed it in the
    /// arguments. Fallback for v0.5.0: look at THIS process's env (won't work
    /// in practice — left here as the hook for the spec'd behaviour). The
    /// Phase-5 plan calls for the host process to track this per-client; this
    /// stub keeps the wire surface honest. firepit_send_to without a known
    /// sender returns a clear error rather than a wrong-from line.
    /// </summary>
    private static string? ReadEnvProjectName() =>
        Environment.GetEnvironmentVariable("FIREPIT_PROJECT_NAME");

    // --- resources/list -------------------------------------------------

    private JsonObject BuildResourcesList()
    {
        var resources = new JsonArray();
        foreach (var def in McpResourceDefinitions.All)
        {
            resources.Add(new JsonObject
            {
                ["uri"]         = def.Uri,
                ["name"]        = def.Name,
                ["description"] = def.Description,
                ["mimeType"]    = def.MimeType,
            });
        }
        return new JsonObject { ["resources"] = resources };
    }

    // --- resources/read -------------------------------------------------

    private async Task<string> HandleResourcesReadAsync(JsonNode? id, JsonNode? paramsNode)
    {
        var uri = paramsNode?["uri"]?.GetValue<string>();
        if (string.IsNullOrEmpty(uri))
            return BuildErrorResponse(id, -32602, "missing 'uri'");

        string text;
        switch (uri)
        {
            case "firepit://projects":
            {
                var projects = await _backend.ListProjectsAsync();
                text = JsonSerializer.Serialize(projects, McpJsonContext.Default.IReadOnlyListProjectInfo);
                break;
            }
            case "firepit://sessions":
            {
                var sessions = await _backend.ListSessionsAsync();
                text = JsonSerializer.Serialize(sessions, McpJsonContext.Default.IReadOnlyListSessionInfo);
                break;
            }
            case "firepit://settings":
                text = await _backend.GetRedactedSettingsAsync();
                break;
            default:
                return BuildErrorResponse(id, -32602, $"unknown resource uri: {uri}");
        }

        var contents = new JsonArray
        {
            new JsonObject
            {
                ["uri"]      = uri,
                ["mimeType"] = "application/json",
                ["text"]     = text,
            },
        };
        return BuildResult(id, new JsonObject { ["contents"] = contents });
    }

    // --- envelope helpers -----------------------------------------------

    private static JsonObject BuildContentJson(string text) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = text,
            },
        },
        ["isError"] = false,
    };

    private static string BuildResult(JsonNode? id, JsonNode result)
    {
        var env = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"]      = id?.DeepClone(),
            ["result"]  = result,
        };
        return env.ToJsonString();
    }

    private static string BuildErrorResponse(JsonNode? id, int code, string message)
    {
        var env = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"]      = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"]    = code,
                ["message"] = message,
            },
        };
        return env.ToJsonString();
    }

    // --- io helpers -----------------------------------------------------

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset + read, count - read), ct);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
