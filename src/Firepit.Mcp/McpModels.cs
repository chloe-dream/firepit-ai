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

// Resource definition returned by resources/list. Tool definitions live as
// internal records next to the catalog (McpToolDefinitionRaw) — they carry
// the inline JSON schema string rather than a parsed JsonElement.

public sealed record McpResourceDefinition(
    string Uri,
    string Name,
    string Description,
    string MimeType);
