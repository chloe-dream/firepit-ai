using System.Text.Json.Serialization;

namespace Firepit.Mcp;

// DTOs the host serialises out as MCP results / resources. Plain records,
// camelCase via the source-gen context.

public sealed record ProjectInfo(
    string Name,
    string Path,
    string AdapterId,
    bool IsOpen,
    string? SessionState);

public sealed record SessionInfo(
    string ProjectName,
    string ProjectPath,
    string State,
    bool IsActive);

public sealed record ToolCallResult(
    bool Ok,
    string? Message = null);

public sealed record InboxWriteResult(
    bool Ok,
    string? Path = null,
    string? Message = null);

// MCP wire-shape envelopes — minimum subset to talk JSON-RPC 2.0.

public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")]      System.Text.Json.JsonElement? Id,
    [property: JsonPropertyName("method")]  string Method,
    [property: JsonPropertyName("params")]  System.Text.Json.JsonElement? Params);

public sealed record JsonRpcError(int Code, string Message);

// Tool / resource definitions returned by tools/list and resources/list.

public sealed record McpToolDefinition(
    string Name,
    string Description,
    System.Text.Json.JsonElement InputSchema);

public sealed record McpResourceDefinition(
    string Uri,
    string Name,
    string Description,
    string MimeType);
